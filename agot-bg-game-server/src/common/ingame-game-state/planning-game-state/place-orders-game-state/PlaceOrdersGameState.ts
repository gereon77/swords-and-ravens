import GameState from "../../../GameState";
import IngameGameState from "../../IngameGameState";
import {ClientMessage} from "../../../../messages/ClientMessage";
import Player from "../../Player";
import Order from "../../game-data-structure/Order";
import Region from "../../game-data-structure/Region";
import World from "../../game-data-structure/World";
import orders from "../../game-data-structure/orders";
import {ServerMessage} from "../../../../messages/ServerMessage";
import EntireGame from "../../../EntireGame";
import {observable} from "mobx";
import BetterMap from "../../../../utils/BetterMap";
import Game from "../../game-data-structure/Game";
import House from "../../game-data-structure/House";
import User from "../../../../server/User";
import PlanningGameState from "../PlanningGameState";
import { PlayerActionType } from "../../game-data-structure/GameLog";
import _ from "lodash";

const MAX_VASSAL_ORDERS = 2;

export default class PlaceOrdersGameState extends GameState<PlanningGameState> {
    // Server-side, the value of the map should never be null.
    // Client-side, the client can receive a null value if it is the order of an other player,
    // it thus represents a face-down order (this player can't see it).
    @observable placedOrders: BetterMap<Region, Order | null> = new BetterMap<Region, Order | null>();
    @observable readyHouses: House[] = [];

    /**
     * Indicates whether this PlaceOrdersGameState phase is for vassals or for non-vassals.
     * PlanningGameState will first go through a phase for non-vassals and then a phase for vassals.
     */
    @observable forVassals: boolean;

    get ingameGameState(): IngameGameState {
        return this.parentGameState.parentGameState;
    }

    get planningGameState(): PlanningGameState {
        return this.parentGameState;
    }

    get game(): Game {
        return this.ingameGameState.game;
    }

    get world(): World {
        return this.game.world;
    }

    get entireGame(): EntireGame {
        return this.ingameGameState.entireGame;
    }

    firstStart(orders = new BetterMap<Region, Order>(), forVassals = false): void {
        this.placedOrders = orders;
        this.forVassals = forVassals;

        if (!this.forVassals || this.ingameGameState.getVassalHouses().length > 0) {
            this.ingameGameState.log({
                type: "planning-phase-began",
                forVassals: this.forVassals
            });
        }

        // Automatically set ready for houses which can't place orders
        this.game.houses.values.forEach(h => {
            // The easiest way is to simply call this.setReady() as it checks for canReady
            // but this from now on disables the possibilty to bypass canReady :(
            this.setReady(this.ingameGameState.getControllerOfHouse(h));
        });
    }

    private setReady(player: Player): void {
        if (!this.canReady(player).status) {
            return;
        }

        this.readyHouses.push(player.house);

        if (!this.forVassals || this.ingameGameState.getVassalsControlledByPlayer(player).length > 0) {
            this.ingameGameState.log({
                type: "player-action",
                house: player.house.id,
                action: PlayerActionType.ORDERS_PLACED
            })
        }

        // Check if all players are ready
        if (this.readyHouses.length == this.ingameGameState.players.size) {
            this.planningGameState.onPlaceOrderFinish(this.forVassals, this.placedOrders as BetterMap<Region, Order>);
        } else {
            this.entireGame.broadcastToClients({
                type: "player-ready",
                userId: player.user.id
            });
        }
    }

    canUnready(player: Player): {status: boolean; reason: string} {
        if (!this.isReady(player)) {
            return {status: false, reason: "not-ready"};
        }

        return {status: true, reason: "player-can-unready"};
    }

    setUnready(player: Player): void {
        if (!this.canUnready(player).status) {
            return;
        }

        this.readyHouses.splice(this.readyHouses.indexOf(player.house), 1);

        this.entireGame.broadcastToClients({
            type: "player-unready",
            userId: player.user.id
        });
    }

    isOrderAvailable(house: House, order: Order): boolean {
        return this.getAvailableOrders(house).includes(order);
    }

    onPlayerMessage(player: Player, message: ClientMessage): void {
        if (message.type == "place-order") {
            const order = message.orderId ? orders.get(message.orderId) : null;
            const region = this.world.regions.get(message.regionId);

            // Retrieve the house for which this order has been placed for.
            const house = _.find(this.getHousesToPutOrdersForPlayer(player), h => this.getPossibleRegionsForOrders(h).includes(region));

            if (house == null) {
                return;
            }

            if (order && !this.isOrderAvailable(house, order)) {
                return;
            }

            if (order && order.type.restrictedTo != null && order.type.restrictedTo != region.type.kind) {
                return;
            }

            // When a player placed or removed an order he is unready
            this.setUnready(player);

            if (order) {
                this.placedOrders.set(region, order);
                player.user.send({
                    type: "order-placed",
                    order: order.id,
                    region: region.id
                });

                this.ingameGameState.players.values.filter(p => p != player).forEach(p => {
                    p.user.send({
                        type: "order-placed",
                        region: region.id,
                        order: null
                    });
                });
            } else {
                if (this.placedOrders.has(region)) {
                    this.placedOrders.delete(region);

                    this.entireGame.broadcastToClients({
                        type: "remove-placed-order",
                        regionId: region.id
                    });
                }
            }
        } else if (message.type == "ready") {
            this.setReady(player);
        } else if (message.type == "unready") {
            this.setUnready(player);
        }
    }

    getPossibleRegionsForOrders(house: House): Region[] {
        const possibleRegions = this.game.world.getControlledRegions(house).filter(r => r.units.size > 0);

        if (!this.forVassals) {
            if (!this.ingameGameState.isVassalHouse(house)) {
                return possibleRegions;
            } else {
                return [];
            }
        }

        if (!this.ingameGameState.isVassalHouse(house)) {
            return [];
        }

        const regionsWithOrders = possibleRegions.filter(r => this.placedOrders.has(r));

        if (regionsWithOrders.length == MAX_VASSAL_ORDERS) {
            return regionsWithOrders;
        }

        return possibleRegions;
    }

    serializeToClient(admin: boolean, player: Player | null): SerializedPlaceOrdersGameState {
        const placedOrders = this.placedOrders.mapOver(r => r.id, (o, r) => {
            // Hide orders that doesn't belong to the player
            // If admin, send all orders.
            const controller = r.getController();
            if (admin || (player && controller != null && (controller == player.house || (this.ingameGameState.isVassalHouse(controller) && this.ingameGameState.isVassalControlledByPlayer(controller, player))))) {
                return o ? o.id : null;
            }
            return null;
        });

        return {
            type: "place-orders",
            placedOrders: placedOrders,
            readyHouses: this.readyHouses.map(h => h.id),
            forVassals: this.forVassals
        };
    }

    /*
     * Common
     */

    canReady(player: Player): {status: boolean; reason: string} {
        if (this.isReady(this.ingameGameState.getControllerOfHouse(player.house))) {
            return {status: false, reason: "already-ready"};
        }

        // Iterate over all the houses the player should put orders for to find
        // an error in one of the houses.
        const possibleError = this.getHousesToPutOrdersForPlayer(player).reduce((state, house) => {
            const possibleRegions = this.getPossibleRegionsForOrders(house);

            if (this.forVassals) {
                const regionsWithOrders = possibleRegions.filter(r => this.placedOrders.has(r));

                if (possibleRegions.length > MAX_VASSAL_ORDERS) {
                    if(regionsWithOrders.length == MAX_VASSAL_ORDERS) {
                        return state;
                    } else if (regionsWithOrders.length > MAX_VASSAL_ORDERS) {
                        return {status: false, reason: "too-much-orders-placed"};
                    }
                }

                if (possibleRegions.every(r => this.placedOrders.has(r))) {
                    return state;
                } else {
                    return {status: false, reason: "not-all-regions-filled"};
                }
            }

            if (possibleRegions.every(r => this.placedOrders.has(r) || this.getAvailableOrders(house, r).length == 0))
            {
                // All possible regions have orders
                return state;
            }

            return {status: false, reason: "not-all-regions-filled"};
        }, null);

        return possibleError ? possibleError : {status: true, reason: ""};
    }

    /**
     * Client
     */
    assignOrder(region: Region, order: Order | null): void {
        this.entireGame.sendMessageToServer({
            type: "place-order",
            regionId: region.id,
            orderId: order ? order.id : null
        });
    }

    ready(): void {
        this.entireGame.sendMessageToServer({
            type: "ready"
        });
    }

    unready(): void {
        this.entireGame.sendMessageToServer({
            type: "unready"
        });
    }

    onServerMessage(message: ServerMessage): void {
        if (message.type == "order-placed") {
            const region = this.world.regions.get(message.region);
            const order = message.order ? orders.get(message.order) : null;

            this.placedOrders.set(region, order);
        } else if (message.type == "remove-placed-order") {
            const region = this.world.regions.get(message.regionId);

            if (this.placedOrders.has(region)) {
                this.placedOrders.delete(region);
            }
        } else if (message.type == "player-ready") {
            const player = this.ingameGameState.players.get(this.entireGame.users.get(message.userId));

            this.readyHouses.push(player.house);
        } else if (message.type == "player-unready") {
            const player = this.ingameGameState.players.get(this.entireGame.users.get(message.userId));

            this.readyHouses.splice(this.readyHouses.indexOf(player.house), 1);
        }
    }

    /**
     * Queries
     */

    /**
     * For a given player, returns all the houses for which `player` must place
     * orders for. Depending on `this.forVassals`, this may be simply the house of the
     * player, or the list of vassals commanded by the player.
     */
    getHousesToPutOrdersForPlayer(player: Player): House[] {
        if (!this.forVassals) {
            return [player.house];
        } else {
            return this.ingameGameState.getVassalsControlledByPlayer(player);
        }
    }

    getNotReadyPlayers(): Player[] {
        return this.ingameGameState.players.values.filter(p => !this.readyHouses.includes(p.house));
    }

    getWaitedUsers(): User[] {
        return this.getNotReadyPlayers().map(p => p.user);
    }

    getOrdersList(house: House): Order[] {
        return this.ingameGameState.game.getOrdersListForHouse(house);
    }

    getAvailableOrders(house: House, region: Region | null = null): Order[] {
        return this.ingameGameState.game.getAvailableOrders(this.placedOrders, house, region, this.parentGameState.planningRestrictions);
    }

    isOrderRestricted(order: Order): boolean {
        return this.parentGameState.planningRestrictions.some(restriction => restriction.restriction(order.type));
    }

    isReady(player: Player): boolean {
        return this.readyHouses.includes(player.house);
    }

    static deserializeFromServer(planning: PlanningGameState, data: SerializedPlaceOrdersGameState): PlaceOrdersGameState {
        const placeOrder = new PlaceOrdersGameState(planning);

        placeOrder.placedOrders = new BetterMap(
            data.placedOrders.map(
                ([regionId, orderId]) => [
                    planning.world.regions.get(regionId),
                    orderId ? orders.get(orderId) : null
                ]
            )
        );
        placeOrder.readyHouses = data.readyHouses.map(hid => planning.ingameGameState.game.houses.get(hid));
        placeOrder.forVassals = data.forVassals;

        return placeOrder;
    }
}

export interface SerializedPlaceOrdersGameState {
    type: "place-orders";
    placedOrders: [string, number | null][];
    readyHouses: string[];
    forVassals: boolean;
}
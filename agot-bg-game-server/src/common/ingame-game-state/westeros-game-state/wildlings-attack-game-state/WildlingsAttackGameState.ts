import GameState from "../../../GameState";
import WesterosGameState from "../WesterosGameState";
import Player from "../../Player";
import BiddingGameState, {SerializedBiddingGameState} from "../bidding-game-state/BiddingGameState";
import Game from "../../game-data-structure/Game";
import House from "../../game-data-structure/House";
import {ClientMessage} from "../../../../messages/ClientMessage";
import {ServerMessage} from "../../../../messages/ServerMessage";
import EntireGame from "../../../EntireGame";
import * as _ from "lodash";
import WildlingCard from "../../game-data-structure/wildling-card/WildlingCard";
import PreemptiveRaidWildlingVictoryGameState
    , {SerializedPreemptiveRaidWildlingVictoryGameState} from "./preemptive-raid-wildling-victory-game-state/PreemptiveRaidWildlingVictoryGameState";
import SimpleChoiceGameState, {SerializedSimpleChoiceGameState} from "../../simple-choice-game-state/SimpleChoiceGameState";
import CrowKillersWildlingVictoryGameState
    , {SerializedCrowKillersWildlingVictoryGameState} from "./crow-killers-wildling-victory-game-state/CrowKillersWildlingVictoryGameState";
import CrowKillersNightsWatchVictoryGameState
    , {SerializedCrowKillersNightsWatchVictoryGameState} from "./crow-killers-nights-watch-victory-game-state/CrowKillersNightsWatchVictoryGameState";
import RattleshirtsRaidersWildlingVictoryGameState
    , {SerializedRattleshirtsRaidersWildlingVictoryGameState} from "./rattleshirts-raiders-wildling-victory-game-state/RattleshirtsRaidersWildlingVictoryGameState";
import MassingOnTheMilkwaterWildlingVictoryGameState
    , {SerializedMassingOnTheMilkwaterWildlingVictoryGameState} from "./massing-on-the-milkwater-wildling-victory-game-state/MassingOnTheMilkwaterWildlingVictoryGameState";
import AKingBeyondTheWallWildlingVictoryGameState, {SerializedAKingBeyondTheWallWildlingVictoryGameState} from "./a-king-beyond-the-wall-wildling-victory-game-state/AKingBeyondTheWallWildlingVictoryGameState";
import AKingBeyondTheWallNightsWatchVictoryGameState, {SerializedAKingBeyondTheWallNightsWatchVictoryGameState} from "./a-king-beyond-the-wall-nights-watch-victory-game-state/AKingBeyondTheWallNightsWatchVictoryGameState";
import MammothRidersWildlingVictoryGameState, {SerializedMammothRidersWildlingVictoryGameState} from "./mammoth-riders-wildling-victory-game-state/MammothRidersWildlingVictoryGameState";
import MammothRidersNightsWatchVictoryGameState, {SerializedMammothRidersNightsWatchVictoryGameState} from "./mammoth-riders-nights-watch-victory-game-state/MammothRidersNightsWatchVictoryGameState";
import TheHordeDescendsWildlingVictoryGameState, {SerializedTheHordeDescendsWildlingVictoryGameState} from "./the-horde-descends-wildling-victory-game-state/TheHordeDescendsWildlingVictoryGameState";
import TheHordeDescendsNightsWatchVictoryGameState, {SerializedTheHordeDescendsNightsWatchVictoryGameState} from "./the-horde-descends-nights-watch-victory-game-state/TheHordeDescendsNightsWatchVictoryGameState";
import IngameGameState from "../../IngameGameState";
import { observable } from "mobx";
import BetterMap from "../../../../utils/BetterMap";
import User from "../../../../server/User";
import { isTakeControlOfEnemyPortRequired } from "../../port-helper/PortHelper";
import TakeControlOfEnemyPortGameState, { SerializedTakeControlOfEnemyPortGameState } from "../../take-control-of-enemy-port-game-state/TakeControlOfEnemyPortGameState";
import ActionGameState from "../../action-game-state/ActionGameState";

export default class WildlingsAttackGameState extends GameState<WesterosGameState,
    BiddingGameState<WildlingsAttackGameState> | SimpleChoiceGameState | PreemptiveRaidWildlingVictoryGameState
    | CrowKillersWildlingVictoryGameState | CrowKillersNightsWatchVictoryGameState
    | RattleshirtsRaidersWildlingVictoryGameState | MassingOnTheMilkwaterWildlingVictoryGameState
    | AKingBeyondTheWallWildlingVictoryGameState | AKingBeyondTheWallNightsWatchVictoryGameState
    | MammothRidersWildlingVictoryGameState | MammothRidersNightsWatchVictoryGameState
    | TheHordeDescendsWildlingVictoryGameState | TheHordeDescendsNightsWatchVictoryGameState
    | TakeControlOfEnemyPortGameState
> {
    @observable  participatingHouses: House[];

    // This field is null before the bidding phase is over,
    // as the wildling card will be drawn after the bidding phase is over.
    wildlingCard: WildlingCard | null;
    @observable wildlingStrength: number;
    _highestBidder: House | null;
    _lowestBidder: House | null;
    @observable biddingResults: [number, House[]][] | null;

    get westerosGameState(): WesterosGameState {
        return this.parentGameState;
    }

    get action(): ActionGameState | null {
        return null;
    }

    get participatingHousesWithoutVassals(): House[] {
        return this.participatingHouses.filter(h => !this.ingame.isVassalHouse(h));
    }

    get excludedHouses(): House[] {
        return _.difference(this.game.houses.values, this.participatingHouses).filter(h => !this.ingame.isVassalHouse(h));
    }

    get totalBid(): number {
        if (this.biddingResults == null) {
            throw new Error();
        }

        return _.sum(this.biddingResults.map(([bid, houses]) => bid * houses.length));
    }

    get nightsWatchWon(): boolean {
        return this.totalBid >= this.wildlingStrength;
    }

    get highestBidders(): House[] {
        if (this.biddingResults == null) {
            throw new Error();
        }

        return this.biddingResults[0][1];
    }

    get lowestBidders(): House[] {
        if (this.biddingResults == null) {
            throw new Error();
        }

        return this.biddingResults[this.biddingResults.length - 1][1];
    }

    get lowestBidder(): House {
        if (!this._lowestBidder) {
            throw new Error();
        }

        return this._lowestBidder;
    }

    get highestBidder(): House {
        if (!this._highestBidder) {
            throw new Error();
        }

        return this._highestBidder;
    }

    get entireGame(): EntireGame {
        return this.westerosGameState.entireGame;
    }

    get game(): Game {
        return this.westerosGameState.game;
    }

    get ingame(): IngameGameState {
        return this.parentGameState.ingame;
    }

    firstStart(wildlingStrength: number, participatingHouses: House[] = []): void {
        this.wildlingStrength = wildlingStrength;

        // Filter out Vassal houses, who never participates in wildling attacks
        this.participatingHouses = participatingHouses.filter(h => !this.ingame.isVassalHouse(h));

        this.setChildGameState(new BiddingGameState(this)).firstStart(this.participatingHouses);
    }

    getTrackWithoutTargaryen(track: House[]): House[] {
        return track.filter(h => h != this.game.targaryen);
    }

    onPlayerMessage(player: Player, message: ClientMessage): void {
        this.childGameState.onPlayerMessage(player, message);
    }

    onServerMessage(message: ServerMessage): void {
        if (message.type == "reveal-wildling-card") {
            this.wildlingCard = this.game.wildlingDeck.find(c => c.id == message.wildlingCard) as WildlingCard;
        } else if (message.type == "reveal-bids") {
            this.biddingResults = message.bids.map(([bid, houses]) => [bid, houses.map(h => this.game.houses.get(h))]);
        } else if (message.type == "wilding-ties-resolved") {
            this._highestBidder = message.highestBidder ? this.game.houses.get(message.highestBidder) : null;
            this._lowestBidder = message.lowestBidder ? this.game.houses.get(message.lowestBidder) : null;
        } else {
            this.childGameState.onServerMessage(message);
        }
    }

    onBiddingGameStateEnd(results: [number, House[]][]): void {
        const resultsWithoutVassals = new BetterMap(results);
        resultsWithoutVassals.keys.forEach(bid => {
            const houses = resultsWithoutVassals.get(bid).filter(h => !this.ingame.isVassalHouse(h));
            if (houses.length > 0) {
                resultsWithoutVassals.set(bid, houses);
            } else {
                resultsWithoutVassals.delete(bid);
            }
        });
        this.biddingResults = resultsWithoutVassals.entries;

        this.westerosGameState.entireGame.broadcastToClients({
            type: "reveal-bids",
            bids: this.biddingResults.map(([bid, houses]) => [bid, houses.map(h => h.id)])
        });

        this.westerosGameState.ingame.log({
            type: "wildling-bidding",
            wildlingStrength: this.wildlingStrength,
            results: this.biddingResults.map(([bid, houses]) => [bid, houses.map(h => h.id)]),
            nightsWatchVictory: this.nightsWatchWon
        });

        if (this.nightsWatchWon) {
            // Wildlings attack has been rebuffed
            // Check if there a single highest bidder or if there needs to be a decision from the iron throne holder
            if (this.highestBidders.length > 1) {
                this.setChildGameState(new SimpleChoiceGameState(this)).firstStart(
                    this.game.ironThroneHolder,
                    "The holder of the Iron Throne must choose between the highest bidders",
                    this.highestBidders.map(h => h.name)
                );
                return;
            }

            this.entireGame.broadcastToClients({
                type: "wilding-ties-resolved",
                highestBidder: this.highestBidders[0].id
            });

            this.proceedNightsWatchWon(this.highestBidders[0]);
        } else {
            // Wildlings attack was successful
            // Check if there a single lowest bidder or if there needs to be a decision from the iron throne holder
            if (this.lowestBidders.length > 1) {
                this.setChildGameState(new SimpleChoiceGameState(this)).firstStart(
                    this.game.ironThroneHolder,
                    "The holder of the Iron Throne must choose between the lowest bidders",
                    this.lowestBidders.map(h => h.name)
                );
                return;
            }

            this.entireGame.broadcastToClients({
                type: "wilding-ties-resolved",
                lowestBidder: this.lowestBidders[0].id
            });

            this.proceedWildlingWon(this.lowestBidders[0]);
        }
    }

    onSimpleChoiceGameStateEnd(choice: number): void {
        this.westerosGameState.ingame.log({
            type: "ties-decided",
            house: this.game.ironThroneHolder.id
        });

        if (this.nightsWatchWon) {
            const highestBidder = this.highestBidders[choice];

            this.entireGame.broadcastToClients({
                type: "wilding-ties-resolved",
                highestBidder: highestBidder.id
            });

            this.westerosGameState.ingame.log({
                type: "highest-bidder-chosen",
                highestBidder: highestBidder.id
            });

            this.proceedNightsWatchWon(highestBidder);
        } else {
            const lowestBidder = this.lowestBidders[choice];

            this.entireGame.broadcastToClients({
                type: "wilding-ties-resolved",
                lowestBidder: lowestBidder.id
            });

            this.westerosGameState.ingame.log({
                type: "lowest-bidder-chosen",
                lowestBidder: lowestBidder.id
            });

            this.proceedWildlingWon(this.lowestBidders[choice]);
        }
    }

    revealTopWildlingCard(): WildlingCard {
        // Reset knowsNextWildlingCard
        this.game.houses.forEach(h => h.knowsNextWildlingCard = false);
        this.entireGame.broadcastToClients({type: "hide-top-wildling-card"});

        // Draw and bury the first card from the wildling deck
        // Before solving issue #1261, wildling cards were drawn after the bidding phase is over.
        // They are now drawn after highest/lowest bidder decision was done, immediately before they are executed.
        // To successfuly migrate the games, if wildlingCard is already present, don't redraw a new one.
        if (!this.wildlingCard) {
            this.wildlingCard = this.game.wildlingDeck.shift() as WildlingCard;
            this.game.wildlingDeck.push(this.wildlingCard);
        }

        // Reveal the wildling card to the players
        this.entireGame.broadcastToClients({
            type: "reveal-wildling-card",
            wildlingCard: this.wildlingCard.id
        });

        this.westerosGameState.ingame.log({
            type: "wildling-card-revealed",
            wildlingCard: this.wildlingCard.id
        });

        return this.wildlingCard;
    }

    proceedNightsWatchWon(highestBidder: House): void {
        this.wildlingCard = this.revealTopWildlingCard();

        this._highestBidder = highestBidder;

        this.wildlingCard.type.executeNightsWatchWon(this);
    }

    proceedWildlingWon(lowestBidder: House): void {
        this.wildlingCard = this.revealTopWildlingCard();

        this._lowestBidder = lowestBidder;

        this.wildlingCard.type.executeWildlingWon(this);
    }

    onWildlingCardExecuteEnd(): void {
        // Change the wildling strength based on the result of wildlings attack
        if (this.nightsWatchWon) {
            this.game.wildlingStrength = 0;
        } else {
            this.game.updateWildlingStrength(-4);
        }
        this.entireGame.broadcastToClients({
            type: "change-wildling-strength",
            wildlingStrength: this.game.wildlingStrength
        });

        const consequence = this.ingame.processPossibleConsequencesOfUnitLossAndCheckWinningConditions();
        if (consequence.victoryConditionsFulfilled) {
            return;
        } else if (consequence.takeOverPort) {
            this.setChildGameState(new TakeControlOfEnemyPortGameState(this))
                .firstStart(consequence.takeOverPort.port, consequence.takeOverPort.newController);
            return;
        }

        this.westerosGameState.onWildlingsAttackGameStateEnd();
    }

    onTakeControlOfEnemyPortFinish(_previousHouse: House | null): void {
        const takeOverRequired = isTakeControlOfEnemyPortRequired(this.parentGameState.ingame);
        if (takeOverRequired) {
            this.setChildGameState(new TakeControlOfEnemyPortGameState(this)).firstStart(takeOverRequired.port, takeOverRequired.newController);
            return;
        }

        this.westerosGameState.onWildlingsAttackGameStateEnd();
    }

    getWaitedUsers(): User[] {
        if (this.childGameState) {
            return this.childGameState.getWaitedUsers();
        }

        return [];
    }

    serializeToClient(admin: boolean, player: Player | null): SerializedWildlingsAttackGameState {
        return {
            type: "wildlings-attack",
            wildlingStrength: this.wildlingStrength,
            childGameState: this.childGameState.serializeToClient(admin, player),
            participatingHouses: this.participatingHouses.map(h => h.id),
            // Only give the wildling card after the bidding phase
            wildlingCard: this.wildlingCard
                ? this.wildlingCard.id
                :  null,
            biddingResults: this.biddingResults ? this.biddingResults.map(([bid, houses]) => ([bid, houses.map(h => h.id)])) : null,
            lowestBidder: this._lowestBidder ? this._lowestBidder.id : null,
            highestBidder: this._highestBidder ? this._highestBidder.id : null
        };
    }

    static deserializeFromServer(westerosGameState: WesterosGameState, data: SerializedWildlingsAttackGameState): WildlingsAttackGameState {
        const wildlingsAttackGameState = new WildlingsAttackGameState(westerosGameState);

        wildlingsAttackGameState.wildlingStrength = data.wildlingStrength;
        wildlingsAttackGameState.participatingHouses = data.participatingHouses.map(hid => westerosGameState.game.houses.get(hid));
        wildlingsAttackGameState.childGameState = wildlingsAttackGameState.deserializeChildGameState(data.childGameState);
        wildlingsAttackGameState.wildlingCard = data.wildlingCard ? wildlingsAttackGameState.game.wildlingDeck.find(c => c.id == data.wildlingCard) as WildlingCard : null;
        wildlingsAttackGameState.biddingResults = data.biddingResults ? data.biddingResults.map(([bid, hids]) => ([bid, hids.map(hid => westerosGameState.game.houses.get(hid))])) : null;
        wildlingsAttackGameState._lowestBidder = data.lowestBidder ? westerosGameState.game.houses.get(data.lowestBidder) : null;
        wildlingsAttackGameState._highestBidder = data.highestBidder ? westerosGameState.game.houses.get(data.highestBidder) : null;


        return wildlingsAttackGameState;
    }

    deserializeChildGameState(data: SerializedWildlingsAttackGameState["childGameState"]): WildlingsAttackGameState["childGameState"] {
        switch(data.type) {
            case "bidding":
                return BiddingGameState.deserializeFromServer(this, data);
            case "crow-killers-wildling-victory":
                return CrowKillersWildlingVictoryGameState.deserializeFromServer(this, data);
            case "preemptive-raid-wildling-victory":
                return PreemptiveRaidWildlingVictoryGameState.deserializeFromServer(this, data);
            case "simple-choice":
                return SimpleChoiceGameState.deserializeFromServer(this, data);
            case "crow-killers-nights-watch-victory":
                return CrowKillersNightsWatchVictoryGameState.deserializeFromServer(this, data);
            case "rattleshirts-raiders-wildling-victory":
                return RattleshirtsRaidersWildlingVictoryGameState.deserializeFromServer(this, data);
            case "massing-on-the-milkwater-wildling-victory":
                return MassingOnTheMilkwaterWildlingVictoryGameState.deserializeFromServer(this, data);
            case "a-king-beyond-the-wall-wildling-victory":
                return AKingBeyondTheWallWildlingVictoryGameState.deserializeFromServer(this, data);
            case "a-king-beyond-the-wall-nights-watch-victory":
                return AKingBeyondTheWallNightsWatchVictoryGameState.deserializeFromServer(this, data);
            case "mammoth-riders-nights-watch-victory":
                return MammothRidersNightsWatchVictoryGameState.deserializeFromServer(this, data);
            case "mammoth-riders-wildling-victory":
                return MammothRidersWildlingVictoryGameState.deserializeFromServer(this, data);
            case "the-horde-descends-wildling-victory":
                return TheHordeDescendsWildlingVictoryGameState.deserializeFromServer(this, data);
            case "the-horde-descends-nights-watch-victory":
                return TheHordeDescendsNightsWatchVictoryGameState.deserializeFromServer(this, data);
            case "take-control-of-enemy-port":
                return TakeControlOfEnemyPortGameState.deserializeFromServer(this, data);
        }
    }
}

export interface SerializedWildlingsAttackGameState {
    type: "wildlings-attack";
    wildlingStrength: number;
    participatingHouses: string[];
    childGameState: SerializedBiddingGameState | SerializedSimpleChoiceGameState | SerializedPreemptiveRaidWildlingVictoryGameState
        | SerializedCrowKillersWildlingVictoryGameState | SerializedCrowKillersNightsWatchVictoryGameState
        | SerializedRattleshirtsRaidersWildlingVictoryGameState | SerializedMassingOnTheMilkwaterWildlingVictoryGameState
        | SerializedAKingBeyondTheWallWildlingVictoryGameState | SerializedAKingBeyondTheWallNightsWatchVictoryGameState
        | SerializedMammothRidersWildlingVictoryGameState | SerializedMammothRidersNightsWatchVictoryGameState
        | SerializedTheHordeDescendsWildlingVictoryGameState | SerializedTheHordeDescendsNightsWatchVictoryGameState
        | SerializedTakeControlOfEnemyPortGameState;
    wildlingCard: number | null;
    biddingResults: [number, string[]][] | null;
    highestBidder: string | null;
    lowestBidder: string | null;
}

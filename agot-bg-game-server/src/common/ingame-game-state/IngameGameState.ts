import EntireGame, { NotificationType } from "../EntireGame";
import GameState from "../GameState";
import {ClientMessage} from "../../messages/ClientMessage";
import {ServerMessage} from "../../messages/ServerMessage";
import User from "../../server/User";
import World from "./game-data-structure/World";
import Player, {SerializedPlayer} from "./Player";
import Region from "./game-data-structure/Region";
import PlanningGameState, {SerializedPlanningGameState} from "./planning-game-state/PlanningGameState";
import ActionGameState, {SerializedActionGameState} from "./action-game-state/ActionGameState";
import Order from "./game-data-structure/Order";
import Game, {MIN_PLAYER_COUNT_WITH_VASSALS, SerializedGame} from "./game-data-structure/Game";
import WesterosGameState, {SerializedWesterosGameState} from "./westeros-game-state/WesterosGameState";
import createGame from "./game-data-structure/createGame";
import BetterMap from "../../utils/BetterMap";
import House from "./game-data-structure/House";
import Unit from "./game-data-structure/Unit";
import PlanningRestriction from "./game-data-structure/westeros-card/planning-restriction/PlanningRestriction";
import GameLogManager, {SerializedGameLogManager} from "./game-data-structure/GameLogManager";
import {GameLogData} from "./game-data-structure/GameLog";
import GameEndedGameState, {SerializedGameEndedGameState} from "./game-ended-game-state/GameEndedGameState";
import UnitType from "./game-data-structure/UnitType";
import WesterosCard from "./game-data-structure/westeros-card/WesterosCard";
import Vote, { SerializedVote, VoteState } from "./vote-system/Vote";
import VoteType, { CancelGame, EndGame, ReplacePlayer, ReplacePlayerByVassal } from "./vote-system/VoteType";
import { v4 } from "uuid";
import CancelledGameState, { SerializedCancelledGameState } from "../cancelled-game-state/CancelledGameState";
import HouseCard from "./game-data-structure/house-card/HouseCard";
import { observable } from "mobx";
import _ from "lodash";
import DraftHouseCardsGameState, { SerializedDraftHouseCardsGameState } from "./draft-house-cards-game-state/DraftHouseCardsGameState";
import CombatGameState from "./action-game-state/resolve-march-order-game-state/combat-game-state/CombatGameState";
import DeclareSupportGameState from "./action-game-state/resolve-march-order-game-state/combat-game-state/declare-support-game-state/DeclareSupportGameState";
import ThematicDraftHouseCardsGameState, { SerializedThematicDraftHouseCardsGameState } from "./thematic-draft-house-cards-game-state/ThematicDraftHouseCardsGameState";
import DraftInfluencePositionsGameState, { SerializedDraftInfluencePositionsGameState } from "./draft-influence-positions-game-state/DraftInfluencePositionsGameState";

export const NOTE_MAX_LENGTH = 5000;

export default class IngameGameState extends GameState<
    EntireGame,
    WesterosGameState | PlanningGameState | ActionGameState | CancelledGameState | GameEndedGameState
    | DraftHouseCardsGameState | ThematicDraftHouseCardsGameState | DraftInfluencePositionsGameState
> {
    players: BetterMap<User, Player> = new BetterMap<User, Player>();
    game: Game;
    gameLogManager: GameLogManager = new GameLogManager(this);
    votes: BetterMap<string, Vote> = new BetterMap();
    @observable rerender = 0;

    get entireGame(): EntireGame {
        return this.parentGameState;
    }

    get world(): World {
        return this.game.world;
    }

    get sortedByLeadingPlayers(): Player[] {
        return this.game.getPotentialWinners().map(h => this.getControllerOfHouse(h));
    }

    constructor(entireGame: EntireGame) {
        super(entireGame);
    }

    beginGame(housesToCreate: string[], futurePlayers: BetterMap<string, User>): void {
        this.game = createGame(this, housesToCreate, futurePlayers.keys);
        this.players = new BetterMap(futurePlayers.map((house, user) => [user, new Player(user, this.game.houses.get(house))]));

        this.log({
            type: "user-house-assignments",
            assignments: futurePlayers.map((house, user) => [house, user.id]) as [string, string][]
        });

        if (this.entireGame.gameSettings.draftHouseCards) {
            this.beginDraftingHouseCards();
        } else {
            this.beginNewTurn();
        }
    }

    beginDraftingHouseCards(): void {
        if (this.entireGame.gameSettings.thematicDraft) {
            this.setChildGameState(new ThematicDraftHouseCardsGameState(this)).firstStart();
        } else {
            this.setChildGameState(new DraftHouseCardsGameState(this)).firstStart();
        }
    }

    proceedDraftingInfluencePositions(vassalsOnInfluenceTracks: House[][]): void {
        this.setChildGameState(new DraftInfluencePositionsGameState(this)).firstStart(vassalsOnInfluenceTracks);
    }

    log(data: GameLogData): void {
        this.gameLogManager.log(data);
    }

    onActionGameStateFinish(): void {
        this.beginNewTurn();
    }

    onWesterosGameStateFinish(planningRestrictions: PlanningRestriction[]): void {
        this.proceedPlanningGameState(planningRestrictions);
    }

    broadcastCustom(f: (player: Player | null) => ServerMessage): void {
        this.entireGame.broadcastCustomToClients(u => {
            const player = this.players.has(u) ? this.players.get(u) : null;

            return f(player);
        });
    }

    proceedPlanningGameState(planningRestrictions: PlanningRestriction[] = []): void {
        this.game.vassalRelations = new BetterMap();
        this.broadcastVassalRelations();
        this.setChildGameState(new PlanningGameState(this)).firstStart(planningRestrictions);
    }

    proceedToActionGameState(placedOrders: BetterMap<Region, Order>, planningRestrictions: PlanningRestriction[]): void {
        // this.placedOrders is of type Map<Region, Order | null> but ActionGameState.firstStart
        // accepts Map<Region, Order>. Server-side, there should never be null values in the map,
        // so it can be converted safely.
        this.setChildGameState(new ActionGameState(this)).firstStart(placedOrders, planningRestrictions);
    }

    beginNewTurn(): void {
        if (this.game.turn == this.game.maxTurns) {
            const winner = this.game.getPotentialWinner();
            this.setChildGameState(new GameEndedGameState(this)).firstStart(winner);
            return;
        }

        if (this.game.turn != 0 && this.game.turn % 10 == 0) {
            // Refresh Westeros deck 3 after every 10th round
            const deck3 = this.game.westerosDecks[2];
            deck3.forEach(wc => wc.discarded = false);
            this.game.westerosDecks[2] = _.shuffle(deck3);

            this.broadcastWesterosDecks();

            // Reshuffle the wildling deck
            this.game.wildlingDeck = _.shuffle(this.game.wildlingDeck);
            this.game.houses.forEach(h => h.knowsNextWildlingCard = false);
            this.entireGame.broadcastToClients({type: "hide-top-wildling-card"});
        }

        this.game.turn++;
        this.log({type: "turn-begin", turn: this.game.turn});

        this.game.valyrianSteelBladeUsed = false;

        // Unwound each units
        this.world.regions.forEach(r => r.units.forEach(u => u.wounded = false));

        this.entireGame.broadcastToClients({
            type: "new-turn"
        });

        if (this.game.turn > 1) {
            this.setChildGameState(new WesterosGameState(this)).firstStart();
        } else {
            // No Westeros phase during the first turn
            this.proceedPlanningGameState();
        }
    }

    onClientMessage(user: User, message: ClientMessage): void {
        if (message.type == "cancel-vote") {
            const vote = this.votes.get(message.vote);

            vote.cancelVote();
        } else if (message.type == "launch-replace-player-vote") {
            const player = this.players.get(this.entireGame.users.get(message.player));

            if (!this.canLaunchReplacePlayerVote(user).result) {
                return;
            }

            this.createVote(user, new ReplacePlayer(user, player.user, player.house));
        } else if (this.players.has(user)) {
            const player = this.players.get(user);

            this.onPlayerMessage(player, message);
        }
    }

    onPlayerMessage(player: Player, message: ClientMessage): void {
        if (message.type == "vote") {
            const vote = this.votes.get(message.vote);

            if (vote.state != VoteState.ONGOING || !vote.participatingHouses.includes(player.house)) {
                return;
            }

            vote.votes.set(player.house, message.choice);

            this.entireGame.broadcastToClients({
                type: "vote-done",
                vote: vote.id,
                voter: player.house.id,
                choice: message.choice
            });

            vote.checkVoteFinished();
        } else if (message.type == "launch-cancel-game-vote") {
            if (this.canLaunchCancelGameVote(player).result) {
                this.createVote(
                    player.user,
                    new CancelGame()
                );
            }
        } else if (message.type == "launch-end-game-vote") {
            if (this.canLaunchEndGameVote(player).result) {
                this.createVote(
                    player.user,
                    new EndGame()
                );
            }
        } else if (message.type == "update-note") {
            player.note = message.note.substring(0, NOTE_MAX_LENGTH);
        } else if (message.type == "launch-replace-player-by-vassal-vote") {
            const playerToReplace = this.players.get(this.entireGame.users.get(message.player));

            if (!this.canLaunchReplacePlayerVote(player.user, true).result) {
                return;
            }

            this.createVote(player.user, new ReplacePlayerByVassal(playerToReplace.user, playerToReplace.house));
        } else if (message.type == "gift-power-tokens") {
            if (!this.canGiftPowerTokens()) {
                return;
            }

            const toHouse = this.game.houses.get(message.toHouse);

            if (!this.isVassalHouse(toHouse)
                    && player.house != toHouse
                    && message.powerTokens > 0
                    && message.powerTokens <= player.house.powerTokens) {
                this.changePowerTokens(player.house, -message.powerTokens);
                this.changePowerTokens(toHouse, message.powerTokens);
                this.log({
                    type: "power-tokens-gifted",
                    house: player.house.id,
                    affectedHouse: toHouse.id,
                    powerTokens: message.powerTokens
                });
            }
        } else {
            this.childGameState.onPlayerMessage(player, message);
        }
    }

    createVote(initiator: User, type: VoteType): Vote {
        const vote = new Vote(this, v4(), this.players.values.map(p => p.house), initiator, type);

        this.votes.set(vote.id, vote);

        this.entireGame.broadcastToClients({
            type: "vote-started",
            vote: vote.serializeToClient(false, null)
        });

        this.entireGame.notifyUsers(_.without(this.players.keys, initiator), NotificationType.NEW_VOTE_STARTED);

        return vote;
    }

    getControllerOfHouse(house: House): Player {
        if (this.isVassalHouse(house)) {
            const suzerainHouse = this.game.vassalRelations.tryGet(house, null);

            if (suzerainHouse == null) {
                throw new Error(`getControllerOfHouse(${house.name}) failed as there is no suzerainHouse`);
            }

            return this.getControllerOfHouse(suzerainHouse);
        } else {
            const player = this.players.values.find(p => p.house == house);

            if (player == null) {
                throw new Error(`getControllerOfHouse(${house.name}) failed due to a fatal error`);
            }

            return player;
        }
    }

    getNextInTurnOrder(house: House | null, except: House | null = null): House {
        const turnOrder = this.game.getTurnOrder();

        if (house == null) {
            return turnOrder[0];
        }

        const i = turnOrder.indexOf(house);

        const nextHouse = turnOrder[(i + 1) % turnOrder.length];

        if (nextHouse == except) {
            return this.getNextInTurnOrder(nextHouse);
        }

        return nextHouse;
    }

    getNextNonVassalInTurnOrder(house: House | null): House {
        house = this.getNextInTurnOrder(house);

        if (!this.isVassalHouse(house)) {
            return house;
        } else {
            return this.getNextNonVassalInTurnOrder(house);
        }
    }

    changePowerTokens(house: House, delta: number): number {
        const originalValue = house.powerTokens;

        const powerTokensOnBoardCount = this.game.countPowerTokensOnBoard(house);
        const maxPowerTokenCount = this.game.maxPowerTokens - powerTokensOnBoardCount;

        house.powerTokens += delta;
        house.powerTokens = Math.max(0, Math.min(house.powerTokens, maxPowerTokenCount));

        this.entireGame.broadcastToClients({
            type: "change-power-token",
            houseId: house.id,
            powerTokenCount: house.powerTokens
        });

        return house.powerTokens - originalValue;
    }

    transformUnits(region: Region, units: Unit[], targetType: UnitType): Unit[] {
        this.entireGame.broadcastToClients({
            type: "remove-units",
            regionId: region.id,
            unitIds: units.map(u => u.id)
        });

        const transformed = units.map(unit => {
            unit.region.units.delete(unit.id);

            const newUnit = this.game.createUnit(unit.region, targetType, unit.allegiance);
            newUnit.region.units.set(newUnit.id, newUnit);

            newUnit.wounded = unit.wounded;

            return newUnit;
        });

        this.entireGame.broadcastToClients({
            type: "add-units",
            units: [[region.id, transformed.map(u => u.serializeToClient())]]
        });

        return transformed;
    }

    checkVictoryConditions(): boolean {
        if (this.game.areVictoryConditionsFulfilled()) {
            // Game is finished
            const winner = this.game.getPotentialWinner();

            this.log({
                type: "winner-declared",
                winner: winner.id
            });

            this.setChildGameState(new GameEndedGameState(this)).firstStart(winner);

            return true;
        } else {
            return false;
        }
    }

    onServerMessage(message: ServerMessage): void {
        if (message.type == "supply-adjusted") {
            const supplies: [House, number][] = message.supplies.map(([houseId, supply]) => [this.game.houses.get(houseId), supply]);

            supplies.forEach(([house, supply]) => house.supplyLevel = supply);
        } else if (message.type == "change-control-power-token") {
            const region = this.world.regions.get(message.regionId);
            const house = message.houseId ? this.game.houses.get(message.houseId) : null;

            region.controlPowerToken = house;
        } else if (message.type == "change-wildling-strength") {
            this.game.wildlingStrength = message.wildlingStrength;
        } else if (message.type == "add-units") {
            message.units.forEach(([regionId, dataUnits]) => {
                const region = this.world.regions.get(regionId);

                dataUnits.forEach(dataUnit => {
                    const unit = Unit.deserializeFromServer(this.game, dataUnit);
                    unit.region = region;

                    region.units.set(unit.id, unit);
                });
            });
        } else if (message.type == "change-garrison") {
            const region = this.world.regions.get(message.region);

            region.garrison = message.newGarrison;
        } else if (message.type == "remove-units") {
            const region = this.world.regions.get(message.regionId);

            const units = message.unitIds.map(uid => region.units.get(uid));

            units.forEach(unit => region.units.delete(unit.id));
        } else if (message.type == "change-state-house-card") {
            const house = this.game.houses.get(message.houseId);
            const cards = message.cardIds.map(cid => house.houseCards.get(cid));

            cards.forEach(hc => hc.state = message.state);
        } else if (message.type == "move-units") {
            const from = this.world.regions.get(message.from);
            const to = this.world.regions.get(message.to);
            const units = message.units.map(uid => from.units.get(uid));

            units.forEach(u => {
                from.units.delete(u.id);
                to.units.set(u.id, u);
                u.region = to;
            });
        } else if (message.type == "change-power-token") {
            const house = this.game.houses.get(message.houseId);

            house.powerTokens = message.powerTokenCount;
        } else if (message.type == "new-turn") {
            this.game.turn++;
            this.game.valyrianSteelBladeUsed = false;
            this.world.regions.forEach(r => r.units.forEach(u => u.wounded = false));
        } else if (message.type == "add-game-log") {
            this.gameLogManager.logs.push({data: message.data, time: new Date(message.time * 1000)});
        } else if (message.type == "change-tracker") {
            const newOrder = message.tracker.map(hid => this.game.houses.get(hid));

            if (message.trackerI == 0) {
                this.game.ironThroneTrack = newOrder;
            } else if (message.trackerI == 1) {
                this.game.fiefdomsTrack = newOrder;
            } else if (message.trackerI == 2) {
                this.game.kingsCourtTrack = newOrder;
            }
        } else if (message.type == "update-westeros-decks") {
            this.game.westerosDecks = message.westerosDecks.map(wd => wd.map(wc => WesterosCard.deserializeFromServer(wc)));
        } else if (message.type == "hide-top-wildling-card") {
            this.game.houses.forEach(h => h.knowsNextWildlingCard = false);
            this.game.clientNextWildlingCardId = null;
        } else if (message.type == "reveal-top-wildling-card") {
            this.game.houses.get(message.houseId).knowsNextWildlingCard = true;
            this.game.clientNextWildlingCardId = message.cardId;
        } else if (message.type == "vote-started") {
            const vote = Vote.deserializeFromServer(this, message.vote);
            this.votes.set(vote.id, vote);
        } else if (message.type == "vote-cancelled") {
            const vote = this.votes.get(message.vote);
            vote.cancelled = true;
        } else if (message.type == "vote-done") {
            const vote = this.votes.get(message.vote);
            const voter = this.game.houses.get(message.voter);

            vote.votes.set(voter, message.choice);
        } else if (message.type == "player-replaced") {
            const oldPlayer = this.players.get(this.entireGame.users.get(message.oldUser));
            const newUser = message.newUser ? this.entireGame.users.get(message.newUser) : null;
            const newPlayer = newUser ? new Player(newUser, oldPlayer.house) : null;

            if (newUser && newPlayer) {
                this.players.set(newUser, newPlayer);
            }

            this.players.delete(oldPlayer.user);

            this.rerender++;
        } else if (message.type == "vassal-relations") {
            this.game.vassalRelations = new BetterMap(message.vassalRelations.map(([vId, cId]) => [this.game.houses.get(vId), this.game.houses.get(cId)]));
            this.rerender++;
        } else if (message.type == "update-house-cards") {
            const house = this.game.houses.get(message.house);
            house.houseCards = new BetterMap(message.houseCards.map(hc => [hc.id, HouseCard.deserializeFromServer(hc)]));
        } else if (message.type == "update-house-cards-for-drafting") {
            this.game.houseCardsForDrafting = new BetterMap(message.houseCards.map(hc => [hc.id, HouseCard.deserializeFromServer(hc)]));
        } else if (message.type == "update-deleted-house-cards") {
            this.game.deletedHouseCards = new BetterMap(message.houseCards.map(hc => [hc.id, HouseCard.deserializeFromServer(hc)]));
        } else if (message.type == "update-max-turns") {
            this.game.maxTurns = message.maxTurns;
        } else {
            this.childGameState.onServerMessage(message);
        }
    }

    getSpectators(): User[] {
        return this.entireGame.users.values.filter(u => !this.players.keys.includes(u));
    }

    launchCancelGameVote(): void {
        if (window.confirm('Do you want to launch a vote to cancel the game?')) {
            this.entireGame.sendMessageToServer({
                type: "launch-cancel-game-vote"
            });
        }
    }

    launchEndGameVote(): void {
        if (window.confirm('Do you want to launch a vote to end the game after the current round?')) {
            this.entireGame.sendMessageToServer({
                type: "launch-end-game-vote"
            });
        }
    }

    canLaunchCancelGameVote(player: Player | null): {result: boolean; reason: string} {
        const existingVotes = this.votes.values.filter(v => v.state == VoteState.ONGOING && v.type instanceof CancelGame);

        if (existingVotes.length > 0) {
            return {result: false, reason: "already-existing"};
        }

        if (player == null || !this.players.values.includes(player)) {
            return {result: false, reason: "only-players-can-vote"};
        }

        if (this.childGameState instanceof CancelledGameState) {
            return {result: false, reason: "already-cancelled"};
        }

        if (this.childGameState instanceof GameEndedGameState) {
            return {result: false, reason: "already-ended"};
        }

        return {result: true, reason: ""};
    }

    canLaunchEndGameVote(player: Player | null): {result: boolean; reason: string} {
        const existingVotes = this.votes.values.filter(v => v.state == VoteState.ONGOING && v.type instanceof EndGame);

        if (existingVotes.length > 0) {
            return {result: false, reason: "already-existing"};
        }

        if (player == null || !this.players.values.includes(player)) {
            return {result: false, reason: "only-players-can-vote"};
        }

        if (this.childGameState instanceof CancelledGameState) {
            return {result: false, reason: "already-cancelled"};
        }

        if (this.childGameState instanceof GameEndedGameState) {
            return {result: false, reason: "already-ended"};
        }

        if (this.game.turn == this.game.maxTurns) {
            return {result: false, reason: "already-last-turn"};
        }

        return {result: true, reason: ""};
    }

    canLaunchReplacePlayerVote(fromUser: User | null, replaceWithVassal = false): {result: boolean; reason: string} {
        if (!fromUser) {
            return {result: false, reason: "only-authenticated-users-can-vote"};
        }

        if (!replaceWithVassal && this.players.keys.includes(fromUser)) {
            return {result: false, reason: "already-playing"};
        }

        if (replaceWithVassal) {
            if (!this.players.keys.includes(fromUser)) {
                return {result: false, reason: "only-players-can-vote"};
            }

            if (this.players.size - 1 < MIN_PLAYER_COUNT_WITH_VASSALS) {
                return {result: false, reason: "min-player-count-reached"};
            }

            if (this.childGameState instanceof DraftHouseCardsGameState) {
                return {result: false, reason: "ongoing-house-card-drafting"}
            }

            if (this.childGameState instanceof ThematicDraftHouseCardsGameState) {
                return {result: false, reason: "ongoing-house-card-drafting"}
            }
        }

        const existingVotes = this.votes.values.filter(v => v.state == VoteState.ONGOING && (v.type instanceof ReplacePlayer || v.type instanceof ReplacePlayerByVassal));
        if (existingVotes.length > 0) {
            return {result: false, reason: "ongoing-vote"};
        }

        if (this.childGameState instanceof CancelledGameState) {
            return {result: false, reason: "game-cancelled"};
        }

        if (this.childGameState instanceof GameEndedGameState) {
            return {result: false, reason: "game-ended"};
        }

        return {result: true, reason: ""};
    }

    getAssociatedHouseCards(house: House): BetterMap<string, HouseCard> {
        if (!this.isVassalHouse(house)) {
            return house.houseCards;
        } else {
            return this.game.vassalHouseCards;
        }
    }

    launchReplacePlayerVote(player: Player): void {
        this.entireGame.sendMessageToServer({
            type: "launch-replace-player-vote",
            player: player.user.id
        });
    }

    launchReplacePlayerByVassalVote(player: Player): void {
        this.entireGame.sendMessageToServer({
            type: "launch-replace-player-by-vassal-vote",
            player: player.user.id
        });
    }

    getVassalHouses(): House[] {
        return this.game.houses.values.filter(h => this.isVassalHouse(h));
    }

    getNonVassalHouses(): House[] {
        return this.game.houses.values.filter(h => !this.isVassalHouse(h));
    }

    isVassalControlledByPlayer(vassal: House, player: Player): boolean {
        if (!this.isVassalHouse(vassal)) {
            throw new Error();
        }

        return this.game.vassalRelations.tryGet(vassal, null) == player.house;
    }

    getVassalsControlledByPlayer(player: Player): House[] {
        return this.getVassalHouses().filter(h => this.isVassalControlledByPlayer(h, player));
    }

    getControlledHouses(player: Player): House[] {
        const houses  = this.getVassalsControlledByPlayer(player);
        houses.unshift(player.house);
        return houses;
    }

    getNonClaimedVassalHouses(): House[] {
        return this.getVassalHouses().filter(v => !this.game.vassalRelations.has(v));
    }

    isVassalHouse(house: House): boolean {
        return !this.players.values.map(p => p.house).includes(house);
    }

    // Returns (House | null) to support .includes(region.getController())
    // but can safely be casted to House[]
    getOtherVassalFamilyHouses(house: House): (House | null)[] {
        const result: House[] = [];
        if (this.game.vassalRelations.has(house)) {
            // If house is a vassal add its commander ...
            const vassalCommader = this.game.vassalRelations.get(house);
            result.push(vassalCommader);

            // ... and all other vassals except myself
            this.game.vassalRelations.entries.forEach(([vassal, commander]) => {
                if (commander == vassalCommader && vassal != house) {
                    result.push(vassal);
                }
            });
        } else {
            // If house is no vassal add potentially controlled vassals
            this.game.vassalRelations.entries.forEach(([vassal, commander]) => {
                if (commander == house) {
                    result.push(vassal);
                }
            });
        }

        return result;
    }

    getTurnOrderWithoutVassals(): House[] {
        return this.game.getTurnOrder().filter(h => !this.isVassalHouse(h));
    }

    broadcastVassalRelations(): void {
        this.entireGame.broadcastToClients({
            type: "vassal-relations",
            vassalRelations: this.game.vassalRelations.entries.map(([vassal, commander]) => [vassal.id, commander.id])
        });
    }

    broadcastWesterosDecks(): void {
        this.entireGame.broadcastToClients({
            type: "update-westeros-decks",
            westerosDecks: this.game.westerosDecks.map(wd => wd.slice(0, this.game.revealedWesterosCards)
                .concat(_.shuffle(wd.slice(this.game.revealedWesterosCards))).map(wc => wc.serializeToClient()))
        });
    }

    updateNote(note: string): void {
        this.entireGame.sendMessageToServer({
            type: "update-note",
            note: note
        });
    }

    giftPowerTokens(toHouse: House, powerTokens: number): void {
        this.entireGame.sendMessageToServer({
            type: "gift-power-tokens",
            toHouse: toHouse.id,
            powerTokens: powerTokens
        });
    }

    canGiftPowerTokens(): boolean {
        if (!this.entireGame.gameSettings.allowGiftingPowerTokens) {
            return false;
        }

        if (this.entireGame.hasChildGameState(CombatGameState) &&
            !(this.entireGame.leafState instanceof DeclareSupportGameState)) {
            return false;
        }

        return true;
    }

    serializeToClient(admin: boolean, user: User | null): SerializedIngameGameState {
        // If user == null, then the game state needs to be serialized
        // in an "admin" version (i.e. containing all data).
        // Otherwise, provide a serialized version that hides data
        // based on which user is requesting the data.
        const player: Player | null = user
            ? (this.players.has(user)
                ? this.players.get(user)
                : null)
            : null;

        return {
            type: "ingame",
            players: this.players.values.map(p => p.serializeToClient(admin, player)),
            game: this.game.serializeToClient(admin, player != null && player.house.knowsNextWildlingCard),
            gameLogManager: this.gameLogManager.serializeToClient(),
            votes: this.votes.values.map(v => v.serializeToClient(admin, player)),
            childGameState: this.childGameState.serializeToClient(admin, player)
        };
    }

    static deserializeFromServer(entireGame: EntireGame, data: SerializedIngameGameState): IngameGameState {
        const ingameGameState = new IngameGameState(entireGame);

        ingameGameState.game = Game.deserializeFromServer(ingameGameState, data.game);
        ingameGameState.players = new BetterMap(
            data.players.map(p => [entireGame.users.get(p.userId), Player.deserializeFromServer(ingameGameState, p)])
        );
        ingameGameState.votes = new BetterMap(data.votes.map(sv => [sv.id, Vote.deserializeFromServer(ingameGameState, sv)]));
        ingameGameState.gameLogManager = GameLogManager.deserializeFromServer(ingameGameState, data.gameLogManager);
        ingameGameState.childGameState = ingameGameState.deserializeChildGameState(data.childGameState);

        return ingameGameState;
    }

    deserializeChildGameState(data: SerializedIngameGameState["childGameState"]): IngameGameState["childGameState"] {
        switch (data.type) {
            case "westeros":
                return WesterosGameState.deserializeFromServer(this, data);
            case "planning":
                return PlanningGameState.deserializeFromServer(this, data);
            case "action":
                return ActionGameState.deserializeFromServer(this, data);
            case "game-ended":
                return GameEndedGameState.deserializeFromServer(this, data);
            case "cancelled":
                return CancelledGameState.deserializeFromServer(this, data);
            case "draft-house-cards":
                return DraftHouseCardsGameState.deserializeFromServer(this, data);
            case "thematic-draft-house-cards":
                return ThematicDraftHouseCardsGameState.deserializeFromServer(this, data);
            case "draft-influence-positions":
                return DraftInfluencePositionsGameState.deserializeFromServer(this, data);
        }
    }
}

export interface SerializedIngameGameState {
    type: "ingame";
    players: SerializedPlayer[];
    game: SerializedGame;
    votes: SerializedVote[];
    gameLogManager: SerializedGameLogManager;
    childGameState: SerializedPlanningGameState | SerializedActionGameState | SerializedWesterosGameState
        | SerializedGameEndedGameState | SerializedCancelledGameState | SerializedDraftHouseCardsGameState
        | SerializedThematicDraftHouseCardsGameState | SerializedDraftInfluencePositionsGameState;
}

import GameState from "../../../../../../../GameState";
import AfterCombatHouseCardAbilitiesGameState from "../AfterCombatHouseCardAbilitiesGameState";
import Player from "../../../../../../Player";
import {ClientMessage} from "../../../../../../../../messages/ClientMessage";
import {ServerMessage} from "../../../../../../../../messages/ServerMessage";
import SelectUnitsGameState from "../../../../../../select-units-game-state/SelectUnitsGameState";
import SelectHouseCardGameState, {SerializedSelectHouseCardGameState} from "../../../../../../select-house-card-game-state/SelectHouseCardGameState";
import House from "../../../../../../game-data-structure/House";
import CombatGameState from "../../../CombatGameState";
import Game from "../../../../../../game-data-structure/Game";
import HouseCard, {HouseCardState} from "../../../../../../game-data-structure/house-card/HouseCard";

export default class PatchfaceAbilityGameState extends GameState<
    AfterCombatHouseCardAbilitiesGameState["childGameState"],
    SelectHouseCardGameState<PatchfaceAbilityGameState>
> {
    get game(): Game {
        return this.combat().game;
    }

    combat(): CombatGameState {
        return this.parentGameState.combatGameState;
    }

    firstStart(house: House) {
        const enemy = this.combat().getEnemy(house);

        const choosableHouseCards = enemy.houseCards.values.filter(hc => hc.state == HouseCardState.AVAILABLE);

        this.setChildGameState(new SelectHouseCardGameState(this)).firstStart(house, choosableHouseCards);
    }

    onSelectHouseCardFinish(house: House, houseCard: HouseCard): void {
        houseCard.state = HouseCardState.USED;

        this.combat().entireGame.broadcastToClients({
            type: "change-state-house-card",
            houseId: (this.game.houses.values.find(h => h.houseCards.values.includes(houseCard)) as House).id,
            cardIds: [houseCard.id],
            state: HouseCardState.USED
        });

        this.parentGameState.onHouseCardResolutionFinish(house);
    }

    onPlayerMessage(player: Player, message: ClientMessage): void {
        this.childGameState.onPlayerMessage(player, message);
    }

    onServerMessage(message: ServerMessage): void {
        this.childGameState.onServerMessage(message);
    }

    serializeToClient(admin: boolean, player: Player | null): SerializedPatchfaceAbilityGameState {
        return {
            type: "patchface-ability",
            childGameState: this.childGameState.serializeToClient(admin, player)
        };
    }

    static deserializeFromServer(afterCombat: AfterCombatHouseCardAbilitiesGameState["childGameState"], data: SerializedPatchfaceAbilityGameState): PatchfaceAbilityGameState {
        const patchfaceAbilityGameState = new PatchfaceAbilityGameState(afterCombat);

        patchfaceAbilityGameState.childGameState = patchfaceAbilityGameState.deserializeChildGameState(data.childGameState);

        return patchfaceAbilityGameState;
    }

    deserializeChildGameState(data: SerializedPatchfaceAbilityGameState["childGameState"]): SelectHouseCardGameState<PatchfaceAbilityGameState> {
        return SelectHouseCardGameState.deserializeFromServer(this, data);
    }
}

export interface SerializedPatchfaceAbilityGameState {
    type: "patchface-ability";
    childGameState: SerializedSelectHouseCardGameState;
}
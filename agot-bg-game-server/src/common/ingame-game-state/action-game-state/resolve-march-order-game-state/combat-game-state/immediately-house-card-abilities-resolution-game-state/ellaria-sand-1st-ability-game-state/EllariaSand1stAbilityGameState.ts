import _ from "lodash";
import ImmediatelyHouseCardAbilitiesResolutionGameState from "../ImmediatelyHouseCardAbilitiesResolutionGameState";
import GameState from "../../../../../../../common/GameState";
import Game from "../../../../../../../common/ingame-game-state/game-data-structure/Game";
import House from "../../../../../../../common/ingame-game-state/game-data-structure/House";
import { ellariaSand1st } from "../../../../../../../common/ingame-game-state/game-data-structure/house-card/houseCardAbilities";
import Region from "../../../../../../../common/ingame-game-state/game-data-structure/Region";
import Unit from "../../../../../../../common/ingame-game-state/game-data-structure/Unit";
import {
  knight,
  footman
} from "../../../../../../../common/ingame-game-state/game-data-structure/unitTypes";
import IngameGameState from "../../../../../../../common/ingame-game-state/IngameGameState";
import Player from "../../../../../../../common/ingame-game-state/Player";
import SelectUnitsGameState, {
  SerializedSelectUnitsGameState
} from "../../../../../../../common/ingame-game-state/select-units-game-state/SelectUnitsGameState";
import { ClientMessage } from "../../../../../../../messages/ClientMessage";
import { ServerMessage } from "../../../../../../../messages/ServerMessage";
import CombatGameState from "../../CombatGameState";

export default class EllariaSand1stAbilityGameState extends GameState<
  ImmediatelyHouseCardAbilitiesResolutionGameState["childGameState"],
  SelectUnitsGameState<EllariaSand1stAbilityGameState>
> {
  get game(): Game {
    return this.parentGameState.game;
  }

  get combat(): CombatGameState {
    return this.parentGameState.combatGameState;
  }

  get ingame(): IngameGameState {
    return this.parentGameState.parentGameState.parentGameState.parentGameState
      .ingameGameState;
  }

  firstStart(house: House): void {
    const upgradableFootmen = this.getUpgradableFootmen(house);

    if (this.game.getAvailableUnitsOfType(house, knight) == 0) {
      this.ingame.log(
        {
          type: "ellaria-sand-1st-no-knight-available",
          house: house.id
        },
        true
      );

      this.parentGameState.onHouseCardResolutionFinish(house);
    } else if (upgradableFootmen.length == 0) {
      this.ingame.log(
        {
          type: "ellaria-sand-1st-no-footman-available",
          house: house.id
        },
        true
      );

      this.parentGameState.onHouseCardResolutionFinish(house);
    } else {
      this.setChildGameState(new SelectUnitsGameState(this)).firstStart(
        house,
        upgradableFootmen,
        1,
        true
      );
    }
  }

  getUpgradableFootmen(house: House): Unit[] {
    // Assemble a list of all units belonging to the house (supporting or not), and then take the footmen among them
    const units = [...this.combat.houseCombatDatas.get(house).army];
    return units.filter((u) => u.type == footman);
  }

  onSelectUnitsEnd(house: House, selectedUnit: [Region, Unit[]][]): void {
    // Upgrade the footmen to a knight
    // Even tough they should be only one unit in "selectedUnit",
    // the following code is generic for all units in it.

    if (_.flatMap(selectedUnit.map(([_, u]) => u)).length == 0) {
      this.ingame.log({
        type: "house-card-ability-not-used",
        house: house.id,
        houseCard: ellariaSand1st.id
      });
    }

    const houseCombatData = this.combat.houseCombatDatas.get(house);

    selectedUnit.forEach(([region, footmenToRemove]) => {
      // Replace them by knight
      const knightsToAdd = this.ingame.transformUnits(
        region,
        footmenToRemove,
        knight
      );

      if (houseCombatData.region == region) {
        // In case the footman was party of the army,
        // remove it from the army.
        houseCombatData.army = _.without(
          houseCombatData.army,
          ...footmenToRemove
        );

        // If the new knight is part of the attacking army,
        // it will now be part of the army
        houseCombatData.army.push(...knightsToAdd);

        this.entireGame.broadcastToClients({
          type: "combat-change-army",
          house: house.id,
          region: region.id,
          army: houseCombatData.army.map((u) => u.id)
        });
      }

      this.ingame.log({
        type: "ellaria-sand-1st-footman-upgraded-to-knight",
        house: house.id,
        region: region.id
      });
    });

    this.parentGameState.onHouseCardResolutionFinish(this.childGameState.house);
  }

  onPlayerMessage(player: Player, message: ClientMessage): void {
    this.childGameState.onPlayerMessage(player, message);
  }

  onServerMessage(message: ServerMessage): void {
    this.childGameState.onServerMessage(message);
  }

  serializeToClient(
    admin: boolean,
    player: Player | null
  ): SerializedEllariaSand1stAbilityGameState {
    return {
      type: "ellaria-sand-1st-ability",
      childGameState: this.childGameState.serializeToClient(admin, player)
    };
  }

  static deserializeFromServer(
    afterWinnerDeterminationChild: ImmediatelyHouseCardAbilitiesResolutionGameState["childGameState"],
    data: SerializedEllariaSand1stAbilityGameState
  ): EllariaSand1stAbilityGameState {
    const ellariaSand1stAbility = new EllariaSand1stAbilityGameState(
      afterWinnerDeterminationChild
    );

    ellariaSand1stAbility.childGameState =
      ellariaSand1stAbility.deserializeChildGameState(data.childGameState);

    return ellariaSand1stAbility;
  }

  deserializeChildGameState(
    data: SerializedEllariaSand1stAbilityGameState["childGameState"]
  ): SelectUnitsGameState<EllariaSand1stAbilityGameState> {
    return SelectUnitsGameState.deserializeFromServer(this, data);
  }
}

export interface SerializedEllariaSand1stAbilityGameState {
  type: "ellaria-sand-1st-ability";
  childGameState: SerializedSelectUnitsGameState;
}

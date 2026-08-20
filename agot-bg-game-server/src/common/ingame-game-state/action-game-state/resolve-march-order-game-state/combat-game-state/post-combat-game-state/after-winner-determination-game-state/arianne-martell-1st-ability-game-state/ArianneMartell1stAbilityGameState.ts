import GameState from "../../../../../../../GameState";
import AfterWinnerDeterminationGameState from "../AfterWinnerDeterminationGameState";
import Game from "../../../../../../game-data-structure/Game";
import CombatGameState from "../../../CombatGameState";
import House from "../../../../../../game-data-structure/House";
import Player from "../../../../../../Player";
import { ClientMessage } from "../../../../../../../../messages/ClientMessage";
import { ServerMessage } from "../../../../../../../../messages/ServerMessage";
import Region from "../../../../../../game-data-structure/Region";
import ActionGameState from "../../../../../ActionGameState";
import IngameGameState from "../../../../../../IngameGameState";
import Unit from "../../../../../../game-data-structure/Unit";
import _ from "lodash";
import SelectUnitsGameState, {
  SerializedSelectUnitsGameState
} from "../../../../../../select-units-game-state/SelectUnitsGameState";
import { arianneMartell1st } from "../../../../../../game-data-structure/house-card/houseCardAbilities";
import HouseCard from "../../../../../../game-data-structure/house-card/HouseCard";

export default class ArianneMartell1stAbilityGameState extends GameState<
  AfterWinnerDeterminationGameState["childGameState"],
  SelectUnitsGameState<ArianneMartell1stAbilityGameState>
> {
  house: House;
  get game(): Game {
    return this.parentGameState.game;
  }

  get action(): ActionGameState {
    return this.combat.actionGameState;
  }

  get combat(): CombatGameState {
    return this.parentGameState.combatGameState;
  }

  get ingame(): IngameGameState {
    return this.parentGameState.parentGameState.parentGameState.parentGameState
      .ingameGameState;
  }

  get enemy(): House {
    return this.combat.getEnemy(this.house);
  }

  firstStart(house: House): void {
    this.house = house;

    if (this.combat.areCasualtiesPrevented(this.enemy)) {
      this.ingame.log({
        type: "casualties-prevented",
        house: this.enemy.id,
        houseCard: (
          this.combat.houseCombatDatas.get(this.enemy).houseCard as HouseCard
        ).id
      });
      this.parentGameState.onHouseCardResolutionFinish(house);
      return;
    }

    const enemyArmy = this.combat.houseCombatDatas.get(this.enemy).army;

    if (enemyArmy.length == 0) {
      this.ingame.log(
        {
          type: "house-card-ability-not-used",
          house: this.house.id,
          houseCard: arianneMartell1st.id
        },
        true
      );
      this.parentGameState.onHouseCardResolutionFinish(this.house);
      return;
    }

    this.setChildGameState(new SelectUnitsGameState(this)).firstStart(
      this.house,
      enemyArmy,
      1,
      true
    );
  }

  onSelectUnitsEnd(house: House, selectedUnits: [Region, Unit[]][]): void {
    // There will only be one footman in "selectedUnit",
    // but the following code deals with the multiple units present.
    selectedUnits.forEach(([region, units]) => {
      // Remove them from the regions and if necessary from the army of the opponent as well
      const houseCombatData = this.combat.houseCombatDatas.get(this.enemy);
      if (units.some((u) => houseCombatData.army.includes(u))) {
        houseCombatData.army = _.without(houseCombatData.army, ...units);

        this.entireGame.broadcastToClients({
          type: "combat-change-army",
          region: region.id,
          house: this.enemy.id,
          army: houseCombatData.army.map((u) => u.id)
        });
      }

      units.forEach((unit) => {
        region.units.delete(unit.id);
      });

      this.ingame.broadcastRemoveUnits(region, units);

      this.ingame.log({
        type: "arianne-martell-1st-army-unit-killed",
        house: house.id,
        affectedHouse: this.enemy.id,
        unit: units[0].type.id
      });

      // Arianne Martell may cause an orphaned order.
      this.combat.parentGameState.actionGameState.findOrphanedOrdersAndRemoveThem();
    });

    if (selectedUnits.length == 0) {
      this.ingame.log({
        type: "house-card-ability-not-used",
        house: this.house.id,
        houseCard: arianneMartell1st.id
      });
    }

    this.parentGameState.onHouseCardResolutionFinish(house);
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
  ): SerializedArianneMartell1stAbilityGameState {
    return {
      type: "arianne-martell-1st-ability",
      house: this.house.id,
      childGameState: this.childGameState.serializeToClient(admin, player)
    };
  }

  static deserializeFromServer(
    afterWinnerDeterminationChild: AfterWinnerDeterminationGameState["childGameState"],
    data: SerializedArianneMartell1stAbilityGameState
  ): ArianneMartell1stAbilityGameState {
    const arianneMartell1stAbility = new ArianneMartell1stAbilityGameState(
      afterWinnerDeterminationChild
    );

    arianneMartell1stAbility.house =
      afterWinnerDeterminationChild.game.houses.get(data.house);
    arianneMartell1stAbility.childGameState =
      arianneMartell1stAbility.deserializeChildGameState(data.childGameState);

    return arianneMartell1stAbility;
  }

  deserializeChildGameState(
    data: SerializedArianneMartell1stAbilityGameState["childGameState"]
  ): ArianneMartell1stAbilityGameState["childGameState"] {
    switch (data.type) {
      case "select-units":
        return SelectUnitsGameState.deserializeFromServer(this, data);
    }
  }
}

export interface SerializedArianneMartell1stAbilityGameState {
  type: "arianne-martell-1st-ability";
  house: string;
  childGameState: SerializedSelectUnitsGameState;
}

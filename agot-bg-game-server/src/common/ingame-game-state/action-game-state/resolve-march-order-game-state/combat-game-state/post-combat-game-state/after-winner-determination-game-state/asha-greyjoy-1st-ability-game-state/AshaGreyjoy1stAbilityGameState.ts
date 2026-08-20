import GameState from "../../../../../../../GameState";
import AfterWinnerDeterminationGameState from "../AfterWinnerDeterminationGameState";
import SimpleChoiceGameState, {
  SerializedSimpleChoiceGameState
} from "../../../../../../simple-choice-game-state/SimpleChoiceGameState";
import SelectOrdersGameState, {
  SerializedSelectOrdersGameState
} from "../../../../../../select-orders-game-state/SelectOrdersGameState";
import Game from "../../../../../../game-data-structure/Game";
import CombatGameState from "../../../CombatGameState";
import House from "../../../../../../game-data-structure/House";
import Player from "../../../../../../Player";
import { ClientMessage } from "../../../../../../../../messages/ClientMessage";
import { ServerMessage } from "../../../../../../../../messages/ServerMessage";
import Region from "../../../../../../game-data-structure/Region";
import ActionGameState from "../../../../../ActionGameState";
import IngameGameState from "../../../../../../IngameGameState";
import { ashaGreyjoy1st } from "../../../../../../game-data-structure/house-card/houseCardAbilities";
import Order from "../../../../../../game-data-structure/Order";
import ConsolidatePowerOrderType from "../../../../../../../../common/ingame-game-state/game-data-structure/order-types/ConsolidatePowerOrderType";
import SupportOrderType from "../../../../../../../../common/ingame-game-state/game-data-structure/order-types/SupportOrderType";

export default class AshaGreyjoy1stAbilityGameState extends GameState<
  AfterWinnerDeterminationGameState["childGameState"],
  SimpleChoiceGameState | SelectOrdersGameState<AshaGreyjoy1stAbilityGameState>
> {
  get game(): Game {
    return this.parentGameState.game;
  }

  get actionGameState(): ActionGameState {
    return this.combat.actionGameState;
  }

  get combat(): CombatGameState {
    return this.parentGameState.combatGameState;
  }

  get ingame(): IngameGameState {
    return this.parentGameState.parentGameState.parentGameState.parentGameState
      .ingameGameState;
  }

  firstStart(house: House): void {
    if (this.getAvailableRegionsWithOrders(house).length == 0) {
      this.ingame.log({
        type: "asha-greyjoy-1st-no-order-available"
      });

      this.parentGameState.onHouseCardResolutionFinish(house);
      return;
    }

    this.setChildGameState(new SimpleChoiceGameState(this)).firstStart(
      house,
      "",
      ["Activate", "Ignore"]
    );
  }

  onSimpleChoiceGameStateEnd(choice: number): void {
    const house = this.childGameState.house;

    if (choice == 0) {
      const availableRegions = this.getAvailableRegionsWithOrders(house);
      this.setChildGameState(new SelectOrdersGameState(this)).firstStart(
        house,
        availableRegions,
        1
      );
    } else {
      this.ingame.log({
        type: "house-card-ability-not-used",
        house: house.id,
        houseCard: ashaGreyjoy1st.id
      });
      this.parentGameState.onHouseCardResolutionFinish(house);
    }
  }

  getAvailableRegionsWithOrders(house: House): Region[] {
    const enemy = this.combat.getEnemy(house);
    const adjacentRegions = this.combat.world.getNeighbouringRegions(
      this.combat.defendingRegion
    );

    return (
      this.actionGameState
        .getOrdersOfHouse(enemy)
        // Removing the march order used for this attack.
        .filter(
          ([region, order]) =>
            adjacentRegions.includes(region) &&
            (order.type instanceof ConsolidatePowerOrderType ||
              order.type instanceof SupportOrderType)
        )
        .map(([region, _]) => region)
    );
  }

  onSelectOrdersFinish(
    regions: Region[],
    resolvedAutomatically: boolean
  ): void {
    // Remove the order
    regions.forEach((r) => {
      const order = this.actionGameState.removeOrderFromRegion(
        r,
        false,
        undefined,
        undefined,
        "red"
      ) as Order;

      this.ingame.log(
        {
          type: "asha-greyjoy-1st-order-removed",
          house: this.childGameState.house.id,
          affectedHouse: this.combat.getEnemy(this.childGameState.house).id,
          region: r.id,
          order: order.id
        },
        resolvedAutomatically
      );
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
  ): SerializedAshaGreyjoy1stAbilityGameState {
    return {
      type: "asha-greyjoy-1st-ability",
      childGameState: this.childGameState.serializeToClient(admin, player)
    };
  }

  static deserializeFromServer(
    afterWinnerDeterminationChild: AfterWinnerDeterminationGameState["childGameState"],
    data: SerializedAshaGreyjoy1stAbilityGameState
  ): AshaGreyjoy1stAbilityGameState {
    const ashaGreyjoy1stAbility = new AshaGreyjoy1stAbilityGameState(
      afterWinnerDeterminationChild
    );

    ashaGreyjoy1stAbility.childGameState =
      ashaGreyjoy1stAbility.deserializeChildGameState(data.childGameState);

    return ashaGreyjoy1stAbility;
  }

  deserializeChildGameState(
    data: SerializedAshaGreyjoy1stAbilityGameState["childGameState"]
  ): AshaGreyjoy1stAbilityGameState["childGameState"] {
    switch (data.type) {
      case "select-orders":
        return SelectOrdersGameState.deserializeFromServer(this, data);
      case "simple-choice":
        return SimpleChoiceGameState.deserializeFromServer(this, data);
    }
  }
}

export interface SerializedAshaGreyjoy1stAbilityGameState {
  type: "asha-greyjoy-1st-ability";
  childGameState:
    | SerializedSimpleChoiceGameState
    | SerializedSelectOrdersGameState;
}

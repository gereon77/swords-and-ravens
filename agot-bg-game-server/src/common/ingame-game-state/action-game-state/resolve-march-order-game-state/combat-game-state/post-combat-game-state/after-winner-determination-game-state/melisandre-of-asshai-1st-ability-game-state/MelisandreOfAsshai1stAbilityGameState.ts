import GameState from "../../../../../../../GameState";
import Player from "../../../../../../Player";
import { ClientMessage } from "../../../../../../../../messages/ClientMessage";
import { ServerMessage } from "../../../../../../../../messages/ServerMessage";
import SelectHouseCardGameState, {
  SerializedSelectHouseCardGameState
} from "../../../../../../select-house-card-game-state/SelectHouseCardGameState";
import House from "../../../../../../game-data-structure/House";
import CombatGameState from "../../../CombatGameState";
import Game from "../../../../../../game-data-structure/Game";
import HouseCard, {
  HouseCardState
} from "../../../../../../game-data-structure/house-card/HouseCard";
import IngameGameState from "../../../../../../IngameGameState";
import SimpleChoiceGameState, {
  SerializedSimpleChoiceGameState
} from "../../../../../../simple-choice-game-state/SimpleChoiceGameState";
import { melisandreOfAsshai1st } from "../../../../../../game-data-structure/house-card/houseCardAbilities";
import AfterWinnerDeterminationGameState from "../AfterWinnerDeterminationGameState";

export default class MelisandreOfAsshai1stAbilityGameState extends GameState<
  AfterWinnerDeterminationGameState["childGameState"],
  | SimpleChoiceGameState
  | SelectHouseCardGameState<MelisandreOfAsshai1stAbilityGameState>
> {
  get game(): Game {
    return this.combat.game;
  }

  get ingame(): IngameGameState {
    return this.combat.ingameGameState;
  }

  get combat(): CombatGameState {
    return this.parentGameState.combatGameState;
  }

  firstStart(house: House): void {
    if (
      house.powerTokens == 0 ||
      this.getChoosableHouseCards(house).length == 0
    ) {
      this.ingame.log(
        {
          type: "house-card-ability-not-used",
          house: house.id,
          houseCard: melisandreOfAsshai1st.id
        },
        true
      );

      this.parentGameState.onHouseCardResolutionFinish(house);
      return;
    }

    this.setChildGameState(new SimpleChoiceGameState(this)).firstStart(
      house,
      "",
      ["Activate", "Ignore"]
    );
  }

  getChoosableHouseCards(house: House): HouseCard[] {
    const enemy = this.combat.getEnemy(house);
    const combatCards = [
      this.combat.attackerHouseCard,
      this.combat.defenderHouseCard
    ];
    return enemy.houseCards.values.filter(
      (hc) => hc.state == HouseCardState.AVAILABLE && !combatCards.includes(hc)
    );
  }

  onSimpleChoiceGameStateEnd(choice: number): void {
    const house = this.childGameState.house;
    if (choice == 0) {
      this.ingame.changePowerTokens(house, -1);
      this.setChildGameState(new SelectHouseCardGameState(this)).firstStart(
        house,
        this.getChoosableHouseCards(house)
      );
    } else {
      this.ingame.log({
        type: "house-card-ability-not-used",
        house: house.id,
        houseCard: melisandreOfAsshai1st.id
      });
      this.parentGameState.onHouseCardResolutionFinish(house);
    }
  }

  onSelectHouseCardFinish(
    house: House,
    houseCard: HouseCard,
    resolvedAutomatically: boolean
  ): void {
    const affectedHouse = this.game.houses.values.find((h) =>
      h.houseCards.has(houseCard.id)
    ) as House;

    this.ingame.log(
      {
        type: "melisandre-of-asshai-1st-used",
        house: house.id,
        affectedHouse: affectedHouse.id,
        houseCard: houseCard.id
      },
      resolvedAutomatically
    );

    this.parentGameState.parentGameState.parentGameState.markHouseCardAsUsed(
      affectedHouse,
      houseCard
    );

    // No need to check and perform house card handling here as this is a after-winner-determination ability
    // and house card handling will be done later anyways

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
  ): SerializedMelisandreOfAsshai1stAbilityGameState {
    return {
      type: "melisandre-of-asshai-1st-ability",
      childGameState: this.childGameState.serializeToClient(admin, player)
    };
  }

  static deserializeFromServer(
    afterWinnerDetermination: AfterWinnerDeterminationGameState["childGameState"],
    data: SerializedMelisandreOfAsshai1stAbilityGameState
  ): MelisandreOfAsshai1stAbilityGameState {
    const melisandreOfAsshai1stGameState =
      new MelisandreOfAsshai1stAbilityGameState(afterWinnerDetermination);

    melisandreOfAsshai1stGameState.childGameState =
      melisandreOfAsshai1stGameState.deserializeChildGameState(
        data.childGameState
      );

    return melisandreOfAsshai1stGameState;
  }

  deserializeChildGameState(
    data: SerializedMelisandreOfAsshai1stAbilityGameState["childGameState"]
  ):
    | SelectHouseCardGameState<MelisandreOfAsshai1stAbilityGameState>
    | SimpleChoiceGameState {
    switch (data.type) {
      case "simple-choice":
        return SimpleChoiceGameState.deserializeFromServer(this, data);
      case "select-house-card":
        return SelectHouseCardGameState.deserializeFromServer(this, data);
    }
  }
}

export interface SerializedMelisandreOfAsshai1stAbilityGameState {
  type: "melisandre-of-asshai-1st-ability";
  childGameState:
    | SerializedSimpleChoiceGameState
    | SerializedSelectHouseCardGameState;
}

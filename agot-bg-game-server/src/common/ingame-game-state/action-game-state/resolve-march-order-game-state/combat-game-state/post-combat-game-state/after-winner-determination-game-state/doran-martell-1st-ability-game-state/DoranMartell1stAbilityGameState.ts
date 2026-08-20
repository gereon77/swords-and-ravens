import { doranMartell1st } from "../../../../../../../../common/ingame-game-state/game-data-structure/house-card/houseCardAbilities";
import GameState from "../../../../../../../../common/GameState";
import Game from "../../../../../../../../common/ingame-game-state/game-data-structure/Game";
import House from "../../../../../../../../common/ingame-game-state/game-data-structure/House";
import IngameGameState from "../../../../../../../../common/ingame-game-state/IngameGameState";
import Player from "../../../../../../../../common/ingame-game-state/Player";
import SimpleChoiceGameState, {
  SerializedSimpleChoiceGameState
} from "../../../../../../../../common/ingame-game-state/simple-choice-game-state/SimpleChoiceGameState";
import { ClientMessage } from "../../../../../../../../messages/ClientMessage";
import { ServerMessage } from "../../../../../../../../messages/ServerMessage";
import CombatGameState from "../../../CombatGameState";
import AfterWinnerDeterminationGameState from "../AfterWinnerDeterminationGameState";

import BetterMap from "../../../../../../../../utils/BetterMap";

export enum DORAN_MARTELL_1ST_STEP {
  CHOOSE_ACTIVATION,
  CHOOSE_DOMINANCE_TOKEN
}

export default class DoranMartell1stAbilityGameState extends GameState<
  AfterWinnerDeterminationGameState["childGameState"],
  SimpleChoiceGameState
> {
  house: House;
  step: DORAN_MARTELL_1ST_STEP;

  get game(): Game {
    return this.parentGameState.game;
  }

  get combatGameState(): CombatGameState {
    return this.parentGameState.combatGameState;
  }

  get ingame(): IngameGameState {
    return this.game.ingame;
  }

  get enemy(): House {
    return this.combatGameState.getEnemy(this.house);
  }

  firstStart(house: House): void {
    this.house = house;
    const choices = this.getChoices();
    if (choices.size == 0) {
      this.ingame.log(
        {
          type: "house-card-ability-not-used",
          house: house.id,
          houseCard: doranMartell1st.id
        },
        true
      );
      this.parentGameState.onHouseCardResolutionFinish(house);
      return;
    }

    this.step = DORAN_MARTELL_1ST_STEP.CHOOSE_ACTIVATION;

    this.setChildGameState(new SimpleChoiceGameState(this)).firstStart(
      house,
      "",
      ["Activate", "Ignore"]
    );
  }

  onSimpleChoiceGameStateEnd(
    choice: number,
    resolvedAutomatically: boolean
  ): void {
    if (this.step == DORAN_MARTELL_1ST_STEP.CHOOSE_ACTIVATION) {
      if (choice == 0) {
        this.step = DORAN_MARTELL_1ST_STEP.CHOOSE_DOMINANCE_TOKEN;
        const choices = this.getChoices();
        this.setChildGameState(new SimpleChoiceGameState(this)).firstStart(
          this.house,
          "",
          choices.values
        );
      } else {
        this.ingame.log({
          type: "house-card-ability-not-used",
          house: this.house.id,
          houseCard: doranMartell1st.id
        });
        this.parentGameState.onHouseCardResolutionFinish(this.house);
      }
    } else if (this.step == DORAN_MARTELL_1ST_STEP.CHOOSE_DOMINANCE_TOKEN) {
      const choices = this.getChoices();
      const trackIndex = choices.keys[choice];
      const oldHolder = this.game.getTokenHolder(
        this.game.getInfluenceTrackByI(trackIndex),
        trackIndex
      );

      if (trackIndex == 0) {
        this.game.overwrittenIronThroneHolder = this.house;
        this.entireGame.broadcastToClients({
          type: "update-overwritten-dominance-token-holder",
          ironThroneHolder: this.house.id
        });
      } else if (trackIndex == 1) {
        this.game.overwrittenValyrianSteelBladeHolder = this.house;
        this.entireGame.broadcastToClients({
          type: "update-overwritten-dominance-token-holder",
          valyrianSteelBladeHolder: this.house.id
        });
      } else if (trackIndex == 2) {
        this.game.overwrittenRavenHolder = this.house;
        this.entireGame.broadcastToClients({
          type: "update-overwritten-dominance-token-holder",
          ravenHolder: this.house.id
        });
      } else {
        throw new Error("Invalid track index");
      }

      this.ingame.log(
        {
          type: "dominance-token-stolen",
          oldHolder: oldHolder.id,
          newHolder: this.house.id,
          dominanceToken:
            trackIndex == 0
              ? "iron-throne"
              : trackIndex == 1
                ? "valyrian-steel-blade"
                : "raven",
          houseCardId: doranMartell1st.id
        },
        resolvedAutomatically
      );

      this.parentGameState.onHouseCardResolutionFinish(
        this.childGameState.house
      );
    }
  }

  getChoices(): BetterMap<
    0 | 1 | 2,
    "Iron Throne" | "Valyrian Steel Blade" | "Raven"
  > {
    const choices = new BetterMap<
      0 | 1 | 2,
      "Iron Throne" | "Valyrian Steel Blade" | "Raven"
    >();
    if (this.game.ironThroneHolder == this.enemy) choices.set(0, "Iron Throne");
    if (this.game.valyrianSteelBladeHolder == this.enemy)
      choices.set(1, "Valyrian Steel Blade");
    if (this.game.ravenHolder == this.enemy) choices.set(2, "Raven");
    return choices;
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
  ): SerializedDoranMartell1stAbilityGameState {
    return {
      type: "doran-martell-1st-ability",
      house: this.house.id,
      step: this.step,
      childGameState: this.childGameState.serializeToClient(admin, player)
    };
  }

  static deserializeFromServer(
    houseCardResolution: AfterWinnerDeterminationGameState["childGameState"],
    data: SerializedDoranMartell1stAbilityGameState
  ): DoranMartell1stAbilityGameState {
    const doranMartell1stAbilityGameState = new DoranMartell1stAbilityGameState(
      houseCardResolution
    );

    doranMartell1stAbilityGameState.house = houseCardResolution.game.houses.get(
      data.house
    );
    doranMartell1stAbilityGameState.step = data.step;
    doranMartell1stAbilityGameState.childGameState =
      doranMartell1stAbilityGameState.deserializeChildGameState(
        data.childGameState
      );

    return doranMartell1stAbilityGameState;
  }

  deserializeChildGameState(
    data: SerializedDoranMartell1stAbilityGameState["childGameState"]
  ): SimpleChoiceGameState {
    return SimpleChoiceGameState.deserializeFromServer(this, data);
  }
}

export interface SerializedDoranMartell1stAbilityGameState {
  type: "doran-martell-1st-ability";
  childGameState: SerializedSimpleChoiceGameState;
  house: string;
  step: DORAN_MARTELL_1ST_STEP;
}

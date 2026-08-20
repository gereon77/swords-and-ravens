import AfterWinnerDeterminationGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/AfterWinnerDeterminationGameState";
import AshaGreyjoy1stAbilityGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/asha-greyjoy-1st-ability-game-state/AshaGreyjoy1stAbilityGameState";
import House from "../House";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class AshaGreyjoy1stHouseCardAbility extends HouseCardAbility {
  afterWinnerDetermination(
    afterWinnerDetermination: AfterWinnerDeterminationGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    if (afterWinnerDetermination.postCombatGameState.winner == house) {
      afterWinnerDetermination.childGameState
        .setChildGameState(
          new AshaGreyjoy1stAbilityGameState(
            afterWinnerDetermination.childGameState
          )
        )
        .firstStart(house);
      return;
    }
    afterWinnerDetermination.childGameState.onHouseCardResolutionFinish(house);
  }
}

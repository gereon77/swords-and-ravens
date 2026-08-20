import AfterWinnerDeterminationGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/AfterWinnerDeterminationGameState";
import DoranMartell1stAbilityGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/doran-martell-1st-ability-game-state/DoranMartell1stAbilityGameState";
import House from "../House";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class DoranMartell1stHouseCardAbility extends HouseCardAbility {
  afterWinnerDetermination(
    afterWinnerDetermination: AfterWinnerDeterminationGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    if (house == afterWinnerDetermination.postCombatGameState.winner) {
      afterWinnerDetermination.childGameState
        .setChildGameState(
          new DoranMartell1stAbilityGameState(
            afterWinnerDetermination.childGameState
          )
        )
        .firstStart(house);
      return;
    }

    afterWinnerDetermination.childGameState.onHouseCardResolutionFinish(house);
  }
}

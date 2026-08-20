import AfterWinnerDeterminationGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/AfterWinnerDeterminationGameState";
import ArianneMartell1stAbilityGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/arianne-martell-1st-ability-game-state/ArianneMartell1stAbilityGameState";
import House from "../House";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class ArianneMartell1stHouseCardAbility extends HouseCardAbility {
  afterWinnerDetermination(
    afterWinnerDetermination: AfterWinnerDeterminationGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    if (afterWinnerDetermination.postCombatGameState.loser == house) {
      afterWinnerDetermination.childGameState
        .setChildGameState(
          new ArianneMartell1stAbilityGameState(
            afterWinnerDetermination.childGameState
          )
        )
        .firstStart(house);
      return;
    }

    afterWinnerDetermination.childGameState.onHouseCardResolutionFinish(house);
  }
}

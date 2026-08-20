import AfterWinnerDeterminationGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/AfterWinnerDeterminationGameState";
import MelisandreOfAsshai1stAbilityGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/melisandre-of-asshai-1st-ability-game-state/MelisandreOfAsshai1stAbilityGameState";
import House from "../House";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class MelisandreOfAsshai1stHouseCardAbility extends HouseCardAbility {
  afterWinnerDetermination(
    afterWinnerDetermination: AfterWinnerDeterminationGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    afterWinnerDetermination.childGameState
      .setChildGameState(
        new MelisandreOfAsshai1stAbilityGameState(
          afterWinnerDetermination.childGameState
        )
      )
      .firstStart(house);
  }
}

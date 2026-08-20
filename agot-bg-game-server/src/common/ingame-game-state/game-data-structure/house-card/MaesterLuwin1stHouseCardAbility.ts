import AfterWinnerDeterminationGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/AfterWinnerDeterminationGameState";
import MaesterLuwinAbilityGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/maester-luwin-ability-game-state/MaesterLuwinAbilityGameState";
import House from "../House";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class MaesterLuwin1stHouseCardAbility extends HouseCardAbility {
  afterWinnerDetermination(
    afterWinnerDetermination: AfterWinnerDeterminationGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    afterWinnerDetermination.childGameState
      .setChildGameState(
        new MaesterLuwinAbilityGameState(
          afterWinnerDetermination.childGameState
        )
      )
      .firstStart(house);
  }
}

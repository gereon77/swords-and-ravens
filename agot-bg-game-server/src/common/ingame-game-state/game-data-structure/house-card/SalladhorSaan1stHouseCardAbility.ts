import CombatGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/CombatGameState";
import House from "../House";
import Unit from "../Unit";
import { ship } from "../unitTypes";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class SalladhorSaan1stHouseCardAbility extends HouseCardAbility {
  modifyUnitCombatStrength(
    _combat: CombatGameState,
    house: House,
    _houseCard: HouseCard,
    _houseSide: House,
    affectedUnit: Unit,
    support: boolean,
    currentStrength: number
  ): number {
    // Check that it is a non-owned ship
    if (
      affectedUnit.type == ship &&
      affectedUnit.allegiance != house &&
      support
    ) {
      return -currentStrength;
    }

    return 0;
  }
}

import CombatGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/CombatGameState";
import House from "../House";
import Unit from "../Unit";
import { ship } from "../unitTypes";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class VictarionGreyjoy1stHouseCardAbility extends HouseCardAbility {
  modifyUnitCombatStrength(
    combat: CombatGameState,
    house: House,
    _houseCard: HouseCard,
    _houseSide: House,
    affectedUnit: Unit,
    support: boolean,
    _currentStrength: number
  ): number {
    if (
      house == combat.attacker &&
      affectedUnit.allegiance == house &&
      affectedUnit.type == ship &&
      !affectedUnit.wounded &&
      !support
    ) {
      return 1;
    }

    return 0;
  }
}

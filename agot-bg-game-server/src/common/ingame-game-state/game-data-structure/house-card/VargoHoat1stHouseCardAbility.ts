import CombatGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/CombatGameState";
import House from "../House";
import Unit from "../Unit";
import { footman } from "../unitTypes";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class VargoHoat1stHouseCardAbility extends HouseCardAbility {
  modifyUnitCombatStrength(
    combat: CombatGameState,
    house: House,
    _houseCard: HouseCard,
    _houseSide: House,
    affectedUnit: Unit,
    support: boolean,
    _currentStrength: number
  ): number {
    return combat.attacker == house &&
      affectedUnit.allegiance == house &&
      affectedUnit.type == footman &&
      !affectedUnit.wounded &&
      !support
      ? 1
      : 0;
  }
}

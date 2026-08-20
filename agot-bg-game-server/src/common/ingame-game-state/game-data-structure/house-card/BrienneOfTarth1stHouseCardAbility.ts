import ImmediatelyHouseCardAbilitiesResolutionGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/immediately-house-card-abilities-resolution-game-state/ImmediatelyHouseCardAbilitiesResolutionGameState";
import House from "../House";
import DefenseOrderType from "../order-types/DefenseOrderType";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class BrienneOfTarth1stHouseCardAbility extends HouseCardAbility {
  immediatelyResolution(
    immediately: ImmediatelyHouseCardAbilitiesResolutionGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    const combat = immediately.combatGameState;
    if (combat.attacker == house) {
      const defendingOrder = combat.actionGameState.ordersOnBoard.has(
        combat.defendingRegion
      )
        ? combat.actionGameState.ordersOnBoard.get(combat.defendingRegion)
        : null;

      if (defendingOrder && defendingOrder.type instanceof DefenseOrderType) {
        combat.actionGameState.removeOrderFromRegion(
          combat.defendingRegion,
          true,
          undefined,
          false,
          "red"
        );
      }
    }
    immediately.childGameState.onHouseCardResolutionFinish(house);
  }
}

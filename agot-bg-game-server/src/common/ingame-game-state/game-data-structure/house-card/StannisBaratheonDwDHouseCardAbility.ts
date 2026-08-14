import HouseCardAbility from "./HouseCardAbility";
import House from "../House";
import HouseCard from "./HouseCard";
import CancelHouseCardAbilitiesGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/cancel-house-card-abilities-game-state/CancelHouseCardAbilitiesGameState";
import SupportOrderType from "../order-types/SupportOrderType";
import RaidSupportOrderType from "../order-types/RaidSupportOrderType";
import BetterMap from "../../../../utils/BetterMap";

export default class StannisBaratheonDwDHouseCardAbility extends HouseCardAbility {
  cancel(
    cancelResolutionState: CancelHouseCardAbilitiesGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    const combatGameState = cancelResolutionState.combatGameState;
    const actionGameState = combatGameState.actionGameState;
    const game = cancelResolutionState.game;
    if (
      combatGameState.supporters.entries.every(
        ([_supporter, supported]) => supported != house
      )
    ) {
      const regions = game.world
        .getNeighbouringRegions(combatGameState.defendingRegion)
        .filter((r) => actionGameState.ordersOnBoard.has(r))
        .map((r) => ({ r, o: actionGameState.ordersOnBoard.get(r) }))
        .filter(
          ({ o }) =>
            o.type instanceof SupportOrderType ||
            o.type instanceof RaidSupportOrderType
        )
        .map(({ r }) => r);

      regions.forEach((r) =>
        cancelResolutionState.combatGameState.actionGameState.removeOrderFromRegion(
          r,
          true,
          undefined,
          undefined,
          "red"
        )
      );
      combatGameState.supporters = new BetterMap();
    }

    cancelResolutionState.childGameState.onHouseCardResolutionFinish(house);
  }
}

import HouseCardAbility from "./HouseCardAbility";
import House from "../House";
import HouseCard from "./HouseCard";
import ImmediatelyHouseCardAbilitiesResolutionGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/immediately-house-card-abilities-resolution-game-state/ImmediatelyHouseCardAbilitiesResolutionGameState";
import SupportOrderType from "../order-types/SupportOrderType";
import RaidSupportOrderType from "../order-types/RaidSupportOrderType";
import BetterMap from "../../../../utils/BetterMap";

export default class StannisBaratheonDwDHouseCardAbility extends HouseCardAbility {
  immediatelyResolution(
    immediatelyResolution: ImmediatelyHouseCardAbilitiesResolutionGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    const combatGameState = immediatelyResolution.combatGameState;
    const actionGameState = combatGameState.actionGameState;
    const game = immediatelyResolution.game;
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
        immediatelyResolution.combatGameState.actionGameState.removeOrderFromRegion(
          r,
          true,
          undefined,
          undefined,
          "red"
        )
      );
      combatGameState.supporters = new BetterMap();
    }

    immediatelyResolution.childGameState.onHouseCardResolutionFinish(house);
  }
}

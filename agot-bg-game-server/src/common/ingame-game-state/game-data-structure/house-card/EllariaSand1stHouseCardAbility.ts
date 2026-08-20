import EllariaSand1stAbilityGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/immediately-house-card-abilities-resolution-game-state/ellaria-sand-1st-ability-game-state/EllariaSand1stAbilityGameState";
import ImmediatelyHouseCardAbilitiesResolutionGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/immediately-house-card-abilities-resolution-game-state/ImmediatelyHouseCardAbilitiesResolutionGameState";
import House from "../House";
import HouseCard from "./HouseCard";
import HouseCardAbility from "./HouseCardAbility";

export default class EllariaSand1stHouseCardAbility extends HouseCardAbility {
  immediatelyResolution(
    immediatelyResolutionState: ImmediatelyHouseCardAbilitiesResolutionGameState,
    house: House,
    _houseCard: HouseCard
  ): void {
    if (immediatelyResolutionState.combatGameState.defender == house) {
      immediatelyResolutionState.childGameState
        .setChildGameState(
          new EllariaSand1stAbilityGameState(
            immediatelyResolutionState.childGameState
          )
        )
        .firstStart(house);
      return;
    }

    immediatelyResolutionState.childGameState.onHouseCardResolutionFinish(
      house
    );
  }
}

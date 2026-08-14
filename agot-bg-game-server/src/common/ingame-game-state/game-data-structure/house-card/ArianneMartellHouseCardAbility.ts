import HouseCardAbility from "./HouseCardAbility";
import HouseCard from "./HouseCard";
import House from "../House";
import PostCombatGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/PostCombatGameState";
import CombatGameState from "../../action-game-state/resolve-march-order-game-state/combat-game-state/CombatGameState";

export default class ArianneMartellHouseCardAbility extends HouseCardAbility {
  doesPreventAttackingArmyFromMoving(
    postCombat: PostCombatGameState,
    house: House,
    houseCard: HouseCard
  ): boolean {
    if (postCombat.loser == house && postCombat.defender == house) {
      if (houseCard.id == "arianne-martell-asos") {
        return (
          Math.abs(
            postCombat.combat.stats[0].total - postCombat.combat.stats[1].total
          ) <= 2
        );
      }

      return true;
    }

    return false;
  }

  forcesRetreatOfVictoriousDefender(
    postCombat: PostCombatGameState,
    house: House,
    houseCard: HouseCard
  ): boolean {
    const result =
      houseCard.id == "arianne-martell-asos" &&
      postCombat.attacker == house &&
      postCombat.loser == house &&
      Math.abs(
        postCombat.combat.stats[0].total - postCombat.combat.stats[1].total
      ) <= 2;

    if (result) {
      postCombat.combat.ingameGameState.log({
        type: "arianne-martell-force-retreat",
        house: postCombat.loser.id,
        enemyHouse: postCombat.winner.id
      });
    }
    return result;
  }

  forcesValyrianSteelBladeDecision(
    combat: CombatGameState,
    valyrianSteelBladeHolder: House,
    houseCard: HouseCard
  ): boolean {
    if (houseCard.id != "arianne-martell-asos") {
      return false;
    }

    const combatStrengthVsbHolder = combat.getTotalCombatStrength(
      valyrianSteelBladeHolder
    );
    const enemy = combat.getEnemy(valyrianSteelBladeHolder);
    const combatStrengthEnemy = combat.getTotalCombatStrength(enemy);

    return combat.attackerHouseCard == houseCard &&
      valyrianSteelBladeHolder == combat.defender
      ? // return true if the difference in combat strength is exactly 2
        // so VSB holder could use the VSB to make the difference 3
        // and prevent the retreat of the victorious defender
        combatStrengthVsbHolder - combatStrengthEnemy == 2
      : combat.defenderHouseCard == houseCard &&
          valyrianSteelBladeHolder == combat.defender
        ? // return true if the difference in combat strength is exactly 3
          // so VSB holder could use the VSB to make the difference 2
          // and force the attacker to retreat (not entering the area)
          combatStrengthEnemy - combatStrengthVsbHolder == 3
        : false;
  }
}

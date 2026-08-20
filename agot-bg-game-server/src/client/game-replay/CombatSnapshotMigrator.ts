/* eslint-disable @typescript-eslint/no-non-null-assertion */
import {
  GameLogData,
  RetreatRegionChosen
} from "../../common/ingame-game-state/game-data-structure/GameLog";
import EntireGameSnapshot from "./EntireGameSnapshot";
import IngameGameState from "../../common/ingame-game-state/IngameGameState";
import ReplayConstants from "./replay-constants";
import _ from "lodash";
import { removeFirst, pullFirst } from "../../utils/arrayExt";

export interface CombatResultData {
  attacker: string;
  defender: string;
  attackerArmy: string[];
  defenderArmy: string[];
  attackerRegion: string;
  defenderRegion: string;
  retreatRegions: string[];
  winner: string;
  winnerArmy: string[];
  winnerRegion: string;
  loser: string;
  loserArmy: string[];
  loserRegion: string;
  movePrevented: boolean;
  retreatForced: boolean;
  postCombatLogs: GameLogData[];
}

export default class CombatSnapshotMigrator {
  private ingame: IngameGameState;
  private combatResultData: CombatResultData;
  private onCombatResultDataRetrieved: (data: CombatResultData) => void;

  constructor(
    ingame: IngameGameState,
    combatResultDataRetrieved: (data: CombatResultData) => void
  ) {
    this.ingame = ingame;
    this.onCombatResultDataRetrieved = combatResultDataRetrieved;
  }

  migrateCombatResultLog(
    log: GameLogData,
    logIndex: number,
    snap: EntireGameSnapshot
  ): EntireGameSnapshot {
    if (log.type != "combat-result") {
      throw new Error(`Log type must be 'combat-result'`);
    }
    this.combatResultData = this.getCombatResultData(log, logIndex);

    const crd = this.combatResultData;
    this.onCombatResultDataRetrieved(crd);

    // Apply all post-combat logs to the snapshot
    // i.e. logs like "killed-after-combat", "retreat-casualties-suffered", "immediatly-killed-after-combat"
    // so killed units are removed from the snapshot now
    for (let i = 0; i < crd.postCombatLogs.length; i++) {
      const l = crd.postCombatLogs[i];
      snap = this.applyCombatResultEvent(snap, l);
    }

    const attackingRegion = snap.getRegion(crd.attackerRegion);
    const defendingRegion = snap.getRegion(crd.defenderRegion);
    const retreatRegions = crd.retreatRegions.map((regionId) =>
      snap.getRegion(regionId)
    );

    // Arianne Martell ASoS forced retreat of a victorious defender,
    // so both, attacker and defender have to retreat
    if (crd.retreatForced && retreatRegions.length == 2) {
      // Attacker always retreats to attacking region
      if (retreatRegions[0] != attackingRegion) {
        throw new Error(`Attacker retreat region must be the attacking region`);
      }

      // Mark all attacking units as wounded
      attackingRegion.markAllUnitsAsWounded();

      // Now victorious defender has to retreat to the other region
      const retreatRegion = retreatRegions[1];
      while (crd.defenderArmy.length > 0) {
        const unit = crd.defenderArmy.pop();
        if (!unit) break;
        defendingRegion.moveTo(
          retreatRegion,
          unit,
          crd.defender,
          undefined,
          true
        );
      }

      crd.winnerRegion = retreatRegion.id;
    }
    // Perform normal retreat and movement of attacking units after combat:
    else if (crd.attacker == crd.winner) {
      if (retreatRegions.length == 1) {
        const retreatRegion = retreatRegions[0];
        // Retreat defending units
        while (crd.defenderArmy.length > 0) {
          const unit = crd.defenderArmy.pop();
          if (!unit) break;
          defendingRegion.moveTo(
            retreatRegion,
            unit,
            crd.defender,
            undefined,
            true
          );
        }

        crd.loserRegion = retreatRegion.id;
      } else if (retreatRegions.length > 1) {
        throw new Error(
          `Defender retreat region must be unique, but got ${retreatRegions.length} regions`
        );
      }

      // If attacker movement isn't blocked, move attacking units to the defender's region
      if (!crd.movePrevented) {
        const to = snap.getRegion(crd.defenderRegion);
        for (let i = 0; i < crd.attackerArmy.length; i++) {
          const unit = crd.attackerArmy[i];
          attackingRegion.moveTo(to, unit, crd.attacker);
        }

        crd.attackerRegion = crd.defenderRegion;
      }
    } else if (crd.defender == crd.winner) {
      // Attacking units usually retreat to where they came from
      // except Robb Stark forces the attacker to retreat to a specific region
      if (retreatRegions.length == 1) {
        for (let i = 0; i < crd.attackerArmy.length; i++) {
          const unit = crd.attackerArmy[i];
          attackingRegion.moveTo(
            retreatRegions[0],
            unit,
            crd.attacker,
            undefined,
            true
          );
        }

        crd.attackerRegion = retreatRegions[0].id;
      } else {
        // just mark all attacking units as wounded
        attackingRegion.markAllUnitsAsWounded();
      }
    }

    attackingRegion.removeOrder();
    defendingRegion.removeOrder();

    if (!snap.gameSnapshot) return snap;

    const attStats = log.stats[0];
    const defStats = log.stats[1];

    if (attStats.house && attStats.houseCard) {
      const attacker = snap.getHouse(attStats.house);
      attacker.markHouseCardAsUsed(attStats.houseCard);
    }

    if (defStats.house && defStats.houseCard) {
      const defender = snap.getHouse(defStats.house);
      defender.markHouseCardAsUsed(defStats.houseCard);
    }

    return snap;
  }

  private getCombatResultData(
    log: GameLogData,
    logIndex: number
  ): CombatResultData {
    if (log.type != "combat-result") {
      throw new Error(`Log type must be 'combat-result'`);
    }
    const {
      stats: [att, def],
      winner
    } = log;
    const attacker = att.house;
    const defender = def.house;
    const attackerRegion = att.region;
    const defenderRegion = def.region;
    const attackerArmy = [...att.armyUnits];
    const defenderArmy = [...def.armyUnits];
    const winnerArmy = winner === attacker ? attackerArmy : defenderArmy;
    const winnerRegion = winner === attacker ? attackerRegion : defenderRegion;
    const loser = winner === attacker ? defender : attacker;
    const loserArmy = winner === attacker ? defenderArmy : attackerArmy;
    const loserRegion = winner === attacker ? defenderRegion : attackerRegion;

    const logsSlice = this.ingame.gameLogManager.logs.slice(logIndex + 1);
    const relatedCombatResultLogs = _.takeWhile(
      logsSlice,
      (l) => !ReplayConstants.combatTerminationLogTypes.has(l.data.type)
    ).filter((l) => ReplayConstants.relatedCombatResultTypes.has(l.data.type));

    const retreatRegionChosen = _.remove(
      relatedCombatResultLogs,
      (log) => log.data.type == "retreat-region-chosen"
    );

    const arianneMartellMovementPrevented = removeFirst(
      relatedCombatResultLogs,
      (log) => log.data.type == "arianne-martell-prevent-movement"
    );

    const arianneMartellForcedRetreat = removeFirst(
      relatedCombatResultLogs,
      (log) => log.data.type === "arianne-martell-force-retreat"
    );

    const retreatRegions = retreatRegionChosen.map(
      (l) => (l.data as RetreatRegionChosen).regionTo
    );

    return {
      attacker,
      defender,
      attackerArmy,
      defenderArmy,
      attackerRegion,
      defenderRegion,
      winner,
      winnerArmy,
      winnerRegion,
      loser,
      loserArmy,
      loserRegion,
      retreatRegions,
      movePrevented: arianneMartellMovementPrevented != null,
      retreatForced: arianneMartellForcedRetreat != null,
      postCombatLogs: relatedCombatResultLogs.map((l) => l.data)
    };
  }

  private applyCombatResultEvent(
    snap: EntireGameSnapshot,
    log: GameLogData
  ): EntireGameSnapshot {
    const ccd = this.combatResultData;

    switch (log.type) {
      case "immediatly-killed-after-combat": {
        const region = snap.getRegion(ccd.loserRegion);

        for (let i = 0; i < log.killedBecauseWounded.length; i++) {
          const unit = log.killedBecauseWounded[i];
          region.removeUnit(unit, ccd.loser, true);
        }
        for (let i = 0; i < log.killedBecauseCantRetreat.length; i++) {
          const unit = log.killedBecauseCantRetreat[i];
          region.removeUnit(unit, ccd.loser);
          pullFirst(ccd.loserArmy, unit);
        }
        return snap;
      }
      case "killed-after-combat": {
        if (log.house == ccd.loser) {
          const region = snap.getRegion(ccd.loserRegion);
          for (let i = 0; i < log.killed.length; i++) {
            const unit = log.killed[i];
            region.removeUnit(unit, ccd.loser);
            pullFirst(ccd.loserArmy, unit);
          }
        } else if (log.house == ccd.winner) {
          const region = snap.getRegion(ccd.winnerRegion);
          for (let i = 0; i < log.killed.length; i++) {
            const unit = log.killed[i];
            region.removeUnit(unit, ccd.winner);
            pullFirst(ccd.winnerArmy, unit);
          }
        } else {
          throw new Error(`Unable to apply log ${JSON.stringify(log)}`);
        }

        return snap;
      }
      case "retreat-casualties-suffered": {
        if (log.house == ccd.loser) {
          const region = snap.getRegion(ccd.loserRegion);
          for (let i = 0; i < log.units.length; i++) {
            const unit = log.units[i];
            region.removeUnit(unit, ccd.defender);
            pullFirst(ccd.loserArmy, unit);
          }
          return snap;
        } else if (log.house == ccd.winner) {
          const region = snap.getRegion(ccd.winnerRegion);
          for (let i = 0; i < log.units.length; i++) {
            const unit = log.units[i];
            region.removeUnit(unit, ccd.winner);
            pullFirst(ccd.winnerArmy, unit);
          }
          return snap;
        } else {
          throw new Error(`Unable to apply log ${JSON.stringify(log)}`);
        }
      }
      case "renly-baratheon-footman-upgraded-to-knight": {
        const region = snap.getRegion(log.region);
        region.removeUnit("footman", log.house);
        region.createUnit("knight", log.house);
        pullFirst(this.combatResultData.winnerArmy, "footman");
        this.combatResultData.winnerArmy.push("knight");
        return snap;
      }
      case "arianne-martell-1st-army-unit-killed": {
        if (!snap.gameSnapshot) return snap;
        const region = snap.getRegion(this.combatResultData.winnerRegion);
        region.removeUnit(log.unit, log.affectedHouse);
        pullFirst(this.combatResultData.winnerArmy, log.unit);
        return snap;
      }
      case "ser-ilyn-payne-asos-casualty-suffered": {
        if (!snap.gameSnapshot) return snap;
        const region = snap.getRegion(this.combatResultData.loserRegion);
        region.removeUnit(log.unit, log.affectedHouse);
        pullFirst(this.combatResultData.loserArmy, log.unit);
        return snap;
      }
      default:
        throw new Error(`Unhandled combat result log type '${log.type}'`);
    }
  }
}

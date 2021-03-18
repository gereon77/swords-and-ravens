import BetterMap from "../utils/BetterMap";
import unitTypes from "../common/ingame-game-state/game-data-structure/unitTypes";
import staticWorld from "../common/ingame-game-state/game-data-structure/static-data-structure/globalStaticWorld";
import { CrowKillersStep } from "../common/ingame-game-state/westeros-game-state/wildlings-attack-game-state/crow-killers-wildling-victory-game-state/CrowKillersWildlingVictoryGameState";
import { SerializedHouse } from "../common/ingame-game-state/game-data-structure/House";
import { HouseCardState } from "../common/ingame-game-state/game-data-structure/house-card/HouseCard";
import { vassalHouseCards } from "../common/ingame-game-state/game-data-structure/static-data-structure/vassalHouseCards";
import _ from "lodash";

const serializedGameMigrations: {version: string; migrate: (serializeGamed: any) => any}[] = [
    {
        version: "2",
        migrate: (serializedGame: any) => {
            // Migration for #292
            if (serializedGame.childGameState.type == "ingame") {
                const mappings = new BetterMap([
                    ["feast-for-crows", 2],
                    ["put-to-the-sword", 2],
                    ["rains-of-autumn", 2],
                    ["sea-of-storms", 2],
                    ["storm-of-swords", 2],
                    ["web-of-lies", 2],
                    ["wildlings-attack", 2],
                    ["game-of-thrones", 1],
                    ["dark-wings-dark-words", 1],
                    ["clash-of-kings", 1],
                    ["last-days-of-summer", 0],
                    ["mustering", 0],
                    ["winter-is-coming", 0],
                    ["a-throne-of-blades", 0],
                    ["supply", 0],
                ]);
                // The goal is to find a deckI for each card referenced
                // in "(westeros-card-exe)cuted". Anyone will do, as long
                // as it exists.
                const ingame = serializedGame.childGameState;

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "westeros-card-executed")
                    .forEach((log: any) => {
                        log.data.westerosDeckI = mappings.get(log.data.westerosCardType)
                    });
            }

            // Migration for #336
            serializedGame.gameSettings.setupId = "base-game";

            return serializedGame;
        }
    },
    {
        version: "3",
        migrate: (serializedGame: any) => {
          // Migration for #499
          if (serializedGame.childGameState.type == "ingame") {
              const serializedIngameGameState = serializedGame.childGameState;
              if (serializedIngameGameState.childGameState.type == "action") {
                  const serializedActionGameState = serializedIngameGameState.childGameState;
                  if (serializedActionGameState.childGameState.type == "resolve-march-order") {
                      const serializedResolveMarchOrderGameState = serializedActionGameState.childGameState;

                      let lastSelectedId: any;

                      const serializedChildGameState = serializedResolveMarchOrderGameState.childGameState;
                      switch (serializedChildGameState.type) {
                          case "resolve-single-march":
                              lastSelectedId = serializedChildGameState.houseId;
                              break;
                          case "combat":
                              lastSelectedId = serializedChildGameState.attackerId;
                              break;
                          case "take-control-of-enemy-port":
                              lastSelectedId = serializedChildGameState.lastHouseThatResolvedMarchOrderId;
                              break;
                          default:
                              throw new Error("Invalid childGameState type")
                      }

                      // This will get the index of the last player that resolved a march order on the current turn order.
                      // It might result in the pre-fix behavior if the Doran Martell was used as one of the cards in the child game state.
                      serializedResolveMarchOrderGameState.currentTurnOrderIndex = serializedIngameGameState.game.ironThroneTrack.indexOf(lastSelectedId);
                  }
              }
          }


            // Migration for #506
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "combat-result")
                    .forEach((log: any) => {
                        log.data.stats.forEach((s: any) => s.armyUnits = []);
                    });
            }

            // Migration for #501
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "retreat-region-chosen" && log.data.regionTo == null)
                    .forEach((log: any) => {
                        // Convert to a retreat-failed message
                        log.data.type = "retreat-failed";

                        log.data.region = log.data.regionFrom;
                        delete log.data.regionFrom;

                        // Assume the common case for a failed retreat
                        log.data.isAttacker = false;
                    });
            }

            return serializedGame;
        }
    },
    {
        version: "4",
        migrate: (serializedGame: any) => {
            // Migration for #532
            if (serializedGame.childGameState.type == "ingame") {
                // Set max power tokens to the default max of 20
                serializedGame.childGameState.game.maxPowerTokens = 20;
            }

            // Migration for #550
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;

                const unitTypeNameToIdMappings = new BetterMap(unitTypes.entries.map(([utid, ut]) => [ut.name, utid]));
                const houseNameToIdMappings = new BetterMap(ingame.game.houses.map((h: any) => [h.name, h.id]));
                const regionNameToIdMappings = new BetterMap(staticWorld.staticRegions.entries.map(([rid, r]) => [r.name, rid]));

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "immediatly-killed-after-combat")
                    .forEach((log: any) => {
                        const woundedNames: string[] = log.data.killedBecauseWounded;
                        const cannotRetreatNames: string[] = log.data.killedBecauseCantRetreat;
                        const houseName = log.data.house;
                        log.data.house = houseNameToIdMappings.get(houseName);
                        log.data.killedBecauseWounded = woundedNames.map(name => unitTypeNameToIdMappings.get(name));
                        log.data.killedBecauseCantRetreat = cannotRetreatNames.map(name => unitTypeNameToIdMappings.get(name));
                    });

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "killed-after-combat")
                    .forEach((log: any) => {
                        const killedNames: string[] = log.data.killed;
                        const houseName = log.data.house;
                        log.data.house = houseNameToIdMappings.get(houseName);
                        log.data.killed = killedNames.map(name => unitTypeNameToIdMappings.get(name));
                    });

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "ships-destroyed-by-empty-castle")
                    .forEach((log: any) => {
                        const houseName = log.data.house;
                        const portName = log.data.port;
                        const castleName = log.data.castle;
                        log.data.house = houseNameToIdMappings.get(houseName);
                        log.data.port = regionNameToIdMappings.get(portName);
                        log.data.castle = regionNameToIdMappings.get(castleName);
                    });

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "enemy-port-taken")
                    .forEach((log: any) => {
                        const oldControllerName = log.data.oldController;
                        const newControllerName = log.data.newController;
                        const portName = log.data.port;
                        log.data.oldController = houseNameToIdMappings.get(oldControllerName);
                        log.data.newController = houseNameToIdMappings.get(newControllerName);
                        log.data.port = regionNameToIdMappings.get(portName);
                    });

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "retreat-region-chosen")
                    .forEach((log: any) => {
                        const houseName = log.data.house;
                        const regionFromName = log.data.regionFrom;
                        const regionToName = log.data.regionTo;
                        log.data.house = houseNameToIdMappings.get(houseName);
                        log.data.regionFrom = regionNameToIdMappings.get(regionFromName);
                        log.data.regionTo = regionNameToIdMappings.get(regionToName);
                    });

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "retreat-casualties-suffered")
                    .forEach((log: any) => {
                        const houseName = log.data.house;
                        const unitNames: string[] = log.data.units;
                        log.data.house = houseNameToIdMappings.get(houseName);
                        log.data.units = unitNames.map(name => unitTypeNameToIdMappings.get(name));
                    });

                ingame.gameLogManager.logs
                    .filter((log: any) => log.data.type == "retreat-failed")
                    .forEach((log: any) => {
                        const houseName = log.data.house;
                        const regionName = log.data.region;
                        log.data.house = houseNameToIdMappings.get(houseName);
                        log.data.region = regionNameToIdMappings.get(regionName);
                    });
            }

            return serializedGame;
        }
    },
    {
        version: "5",
        migrate: (serializedGame: any) => {
            // Migration for #245
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                const game = ingame.game;

                game.houses.forEach((h: any) => h.knowsNextWildlingCard = false);
                game.clientNextWidllingCardId = null;
            }

            return serializedGame;
        }
    },
    {
        version: "6",
        migrate: (serializedGame: any) => {
            // Migration for #635
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                // Add an empty list of votes in ingame
                ingame.votes = [];

                // Planning phase was changed to store houses and not players
                if (ingame.childGameState.type == "planning") {
                    const planning = ingame.childGameState;

                    const playersToHouse = new BetterMap(ingame.players.map((serializedPlayer: any) => [serializedPlayer.userId, serializedPlayer.houseId]));

                    planning.readyHouses = planning.readyPlayers.map((playerId: string) => playersToHouse.get(playerId));
                }
            }

            return serializedGame;
        }
    },
    {
        version: "7",
        migrate: (serializedGame: any) => {
            // Migration for #690

            // Check if game is currently in "CrowKillersWildlingsVictoryGameState"
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;

                if (ingame.childGameState && ingame.childGameState.type == "westeros") {
                    const westeros = ingame.childGameState;

                    if (westeros.childGameState && westeros.childGameState.type == "wildlings-attack") {
                        const wildlingsAttack = westeros.childGameState;

                        if (wildlingsAttack.childGameState && wildlingsAttack.childGameState == "crow-killers-wildling-victory") {
                            const crowKillersWildlingVictory = wildlingsAttack.childGameState;

                            // We assume no game is in the potential buggy state where we would have to apply KILLING_KNIGHTS
                            crowKillersWildlingVictory.step = CrowKillersStep.DEGRADING_KNIGHTS;
                        }
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "8",
        migrate: (serializedGame: any) => {
            // Migration for #590
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                if (ingame.childGameState && ingame.childGameState.type == "action") {
                    const action = ingame.childGameState;
                    if (action.childGameState && action.childGameState.type == "use-raven") {
                        const useRaven = action.childGameState;
                        if (useRaven.childGameState && useRaven.childGameState.type == "choose-raven-action") {
                            // If game is currently in "choose-raven-action" replace it with new first child state "replace-order"
                            useRaven.childGameState.type = "replace-order"
                        }
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "9",
        migrate: (serializedGame: any) => {
            // Migration for #750
            if (serializedGame.childGameState.type == "ingame") {
                const serializedIngameGameState = serializedGame.childGameState;
                serializedIngameGameState.game.revealedWesterosCards = 0;
            }

            return serializedGame;
        }
    },
    {
        version: "10",
        migrate: (serializedGame: any) => {
            // Migration for #785
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                const game = ingame.game;

                game.houses.forEach((h: SerializedHouse) => {
                    h.houseCards.forEach(([_hcid, shc]) => {
                        shc.disabled = false;
                        shc.disabledAbilityId = null;
                        shc.originalCombatStrength = shc.combatStrength;
                        shc.originalSwordIcons = shc.swordIcons;
                        shc.originalTowerIcons = shc.towerIcons;
                    });
                });
            }

            return serializedGame;
        }
    },
    {
        version: "11",
        migrate: (serializedGame: any) => {
            // Migration for #791
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                if (ingame.childGameState && ingame.childGameState.type == "action") {
                    const action = ingame.childGameState;
                    if (action.childGameState && action.childGameState.type == "resolve-march-order") {
                        const resolveMarchOrders = action.childGameState;
                        if (resolveMarchOrders.childGameState && resolveMarchOrders.childGameState.type == "combat") {
                            const combat = resolveMarchOrders.childGameState;
                            if (combat.childGameState && combat.childGameState.type == "immediately-house-card-abilities-resolution") {
                                const immediatelyHouseCardResolution = combat.childGameState;
                                if (immediatelyHouseCardResolution.childGameState &&
                                    (immediatelyHouseCardResolution.childGameState.type == "aeron-damphair-dwd-ability" || immediatelyHouseCardResolution.childGameState.type == "qyburn-ability")) {
                                        // Aron and Qyburn are now BeforeCombat states => Convert combat.childGameState to BeforeCombat
                                        combat.childGameState.type = "before-combat-house-card-abilities-resolution";
                                }
                            }
                        }
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "12",
        migrate: (serializedGame: any) => {
            // Migration for #795
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                const game = ingame.game;

                game.houses.forEach((h: SerializedHouse) => {
                    h.houseCards.forEach(([hcid, shc]) => {
                        if (hcid == "asha-greyjoy-dwd") {
                            shc.abilityId = null;
                        }
                    });
                });
            }

            return serializedGame;
        }
    },
    {
        version: "13",
        migrate: (serializedGame: any) => {
            // Migration for #808
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                if (ingame.childGameState && ingame.childGameState.type == "action") {
                    const action = ingame.childGameState;
                    if (action.childGameState && action.childGameState.type == "resolve-march-order") {
                        const resolveMarchOrders = action.childGameState;
                        if (resolveMarchOrders.childGameState && resolveMarchOrders.childGameState.type == "combat") {
                            const combat = resolveMarchOrders.childGameState;
                            if (combat.childGameState && combat.childGameState.type == "post-combat") {
                                const postCombat = combat.childGameState;
                                if (postCombat.childGameState && postCombat.childGameState.type == "after-combat-house-card-abilities") {
                                    const afterCombatHouseCardAbilities = postCombat.childGameState;
                                    if (afterCombatHouseCardAbilities.childGameState && afterCombatHouseCardAbilities.childGameState.type == "rodrik-the-reader-ability") {
                                        // Rodrik is now a AfterWinner ability
                                        postCombat.childGameState.type = "after-winner-determination";
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "14",
        migrate: (serializedGame: any) => {
            // Migration for #811
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;

                // Abilities need to have the same id as the house card itself to allow house-card-ability-not-used giving the ability id.
                // In the game log getHouseCardById then returns the house card with the name. We could introduce a getHouseCardByAbilityId at some point.
                // For now lets migrate Melisandre DWD.

                // Fix melisandre => melisandre-dwd ability
                const game = ingame.game;
                game.houses.forEach((h: SerializedHouse) => {
                    h.houseCards.forEach(([hcid, shc]) => {
                        if (hcid == "melisandre-dwd") {
                            shc.abilityId = "melisandre-dwd";
                        }
                    });
                });

                // Fix invalid game logs
                ingame.gameLogManager.logs
                .filter((log: any) => log.data.type == "house-card-ability-not-used")
                .forEach((log: any) => {
                    if (log.data.houseCard == "melisandre") {
                        log.data.houseCard = "melisandre-dwd";
                    }
                });
            }

            return serializedGame;
        }
    },
    {
        version: "15",
        migrate: (serializedGame: any) => {
            // Migration for #450 Vassals
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;

                // Init vassal Relations
                ingame.game.vassalRelations = [];

                // Old planning phase is now child place-orders not for vassals
                if (ingame.childGameState.type == "planning") {
                    const planning = ingame.childGameState;

                    planning.childGameState = {
                        type: "place-orders",
                        readyHouses: planning.readyHouses,
                        placedOrders: planning.placedOrders,
                        forVassals: false
                    };
                }

                if (ingame.childGameState.type == "action") {
                    const action = ingame.childGameState;

                    if (action.childGameState.type == "resolve-raid-order") {
                        const resolveRaid = action.childGameState;
                        resolveRaid.resolvedRaidSupportOrderRegions = [];
                    }

                    if (action.childGameState.type == "resolve-march-order") {
                        const resolveMarch = action.childGameState;
                        if (resolveMarch.childGameState.type == "combat") {
                            const combat = resolveMarch.childGameState;
                            if (combat.childGameState.type == "choose-house-card") {
                                const chooseHouseCard = combat.childGameState;
                                const houses = new BetterMap((ingame.game.houses as SerializedHouse[]).map(h => [h.id, h]));
                                const combatData = combat.houseCombatDatas as [string, {houseCardId: string | null; army: number[]; regionId: string}][];
                                const combatHouses = combatData.map(([h, _hcd]) => h);
                                chooseHouseCard.choosableHouseCards = combatHouses.map(hid => [hid,
                                    houses.get(hid).houseCards.filter(([_hcid, hc]) => hc.state == HouseCardState.AVAILABLE).map(([hcid, _hc]) => hcid)]);
                            }
                        }
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "16",
        migrate: (serializedGame: any) => {
            // Migration for #850
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                const game = ingame.game;

                game.houses.forEach((h: SerializedHouse) => {
                    h.houseCards.forEach(([hcid, shc]) => {
                        if (hcid == "asha-greyjoy-dwd") {
                            shc.towerIcons = 1;
                        }
                    });
                });
            }

            return serializedGame;
        }
    },
    {
        version: "17",
        migrate: (serializedGame: any) => {
            // Migration for #854
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                if (ingame.childGameState && ingame.childGameState.type == "action") {
                    const action = ingame.childGameState;
                    if (action.childGameState && action.childGameState.type == "resolve-march-order") {
                        const resolveMarchOrders = action.childGameState;
                        if (resolveMarchOrders.childGameState && resolveMarchOrders.childGameState.type == "combat") {
                            const combat = resolveMarchOrders.childGameState;
                            if (combat.childGameState && combat.childGameState.type == "before-combat-house-card-abilities-resolution") {
                                const beforeCombatResultion = combat.childGameState;
                                if (beforeCombatResultion.childGameState && beforeCombatResultion.childGameState.type == "aeron-damphair-dwd-ability") {
                                    // aeron child game state is now bidding instead of simple choice
                                    const aeronDwdAbility = beforeCombatResultion.childGameState;
                                    const house = aeronDwdAbility.childGameState.house;
                                    aeronDwdAbility.childGameState = {
                                        type: "bidding",
                                        participatingHouses: [house],
                                        bids: []
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "18",
        migrate: (serializedGame: any) => {
            // Migration for #867
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                if (serializedGame.gameSettings && serializedGame.gameSettings.setupId == "a-dance-with-dragons") {
                    if (ingame.game.turn <= 6) {
                        ingame.game.maxTurns = 6;
                    } else {
                        ingame.game.maxTurns = ingame.game.turn;
                    }
                }
            }

            return serializedGame;
        }
    },
    {
        version: "19",
        migrate: (serializedGame: any) => {
            // Migration for #TBD
            // Make the vassal house cards non static again:
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                ingame.game.vassalHouseCards = vassalHouseCards.map(hc => [hc.id, hc.serializeToClient()]);
            }

            return serializedGame;
        }
    },
    {
        version: "20",
        migrate: (serializedGame: any) => {
            // Migration for #TBD
            if (serializedGame.childGameState.type == "ingame") {
                const ingame = serializedGame.childGameState;
                if (ingame.childGameState.type == "planning") {
                    const planning = ingame.childGameState;
                    if (planning.childGameState.type == "place-orders") {
                        const placeOrders = planning.childGameState;
                        const vassalHouses = new BetterMap(ingame.game.vassalRelations).keys as string[];
                        const nonVassalHouses = _.difference(ingame.game.houses.map((sh: SerializedHouse) => sh.id), vassalHouses);
                        placeOrders.readyHouses = placeOrders.readyHouses.filter((h: string) => placeOrders.forVassals ? vassalHouses.includes(h) : nonVassalHouses.includes(h));
                    }
                }
            }

            return serializedGame;
        }
    }
];

export default serializedGameMigrations;
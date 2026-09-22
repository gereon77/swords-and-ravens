import IngameGameState from "../../IngameGameState";
import GameState from "../../../GameState";
import EntireGame from "../../../EntireGame";
import Player from "../../Player";
import { ClientMessage } from "../../../../messages/ClientMessage";
import { ServerMessage } from "../../../../messages/ServerMessage";
import House from "../../game-data-structure/House";
import Game from "../../game-data-structure/Game";
import HouseCard from "../../game-data-structure/house-card/HouseCard";
import _ from "lodash";
import User from "../../../../server/User";
import { computed } from "mobx";
import DraftGameState, {
  houseCardCombatStrengthAllocations
} from "../DraftGameState";

export default class SelectedRandomDraftGameState extends GameState<DraftGameState> {
  get ingame(): IngameGameState {
    return this.parentGameState.parentGameState;
  }

  get game(): Game {
    return this.ingame.game;
  }

  get entireGame(): EntireGame {
    return this.ingame.entireGame;
  }

  get participatingHouses(): House[] {
    return this.game.nonVassalHouses;
  }

  @computed
  get unreadyPlayers(): Player[] {
    return this.participatingHouses
      .filter((h) => h.houseCards.size < 7)
      .map((h) => this.ingame.getControllerOfHouse(h));
  }

  constructor(draftGameState: DraftGameState) {
    super(draftGameState);
  }

  firstStart(): void {
    this.ingame.log({
      type: "draft-house-cards-began"
    });
  }

  getFilteredHouseCardsForHouse(house: House): HouseCard[] {
    if (!this.participatingHouses.includes(house)) {
      throw new Error(
        "getFilteredHouseCardsForHouse() called for a vassal house!"
      );
    }

    let availableCards = _.sortBy(
      this.game.draftPool.values,
      (hc) => -hc.combatStrength
    );
    house.houseCards.forEach((card) => {
      const countOfCardsWithThisCombatStrength = house.houseCards.values.filter(
        (hc) => hc.combatStrength == card.combatStrength
      ).length;
      if (
        houseCardCombatStrengthAllocations.get(card.combatStrength) ==
        countOfCardsWithThisCombatStrength
      ) {
        availableCards = availableCards.filter(
          (hc) => hc.combatStrength != card.combatStrength
        );
      }
    });

    return availableCards;
  }

  getWaitedUsers(): User[] {
    return this.unreadyPlayers.map((p) => p.user);
  }

  select(houseCard: HouseCard): void {
    this.entireGame.sendMessageToServer({
      type: "select-house-card",
      houseCard: houseCard.id
    });
  }

  onPlayerMessage(player: Player, message: ClientMessage): void {
    if (message.type == "select-house-card") {
      const house = player.house;
      if (
        !this.participatingHouses.includes(house) ||
        house.houseCards.size == 7
      ) {
        return;
      }

      const houseCard = this.parentGameState.game.getHouseCardById(
        message.houseCard
      );

      if (!this.getFilteredHouseCardsForHouse(house).includes(houseCard)) {
        // Resend draft pool to the player to ensure they have the correct state
        player.user.send({
          type: "update-draft-pool",
          houseCards: this.game.draftPool.keys
        });
        return;
      }

      house.houseCards.set(houseCard.id, houseCard);
      this.entireGame.broadcastToClients({
        type: "update-house-cards",
        house: house.id,
        houseCards: house.houseCards.keys
      });

      this.game.draftPool.delete(houseCard.id);
      this.entireGame.broadcastToClients({
        type: "update-draft-pool",
        houseCards: this.game.draftPool.keys
      });

      this.ingame.log({
        type: "house-card-picked",
        house: house.id,
        houseCard: houseCard.id
      });

      if (this.participatingHouses.every((h) => h.houseCards.size == 7)) {
        // Now randomize all house cards between players:
        const selectedHouseCards = this.participatingHouses.flatMap(
          (h) => h.houseCards.values
        );

        this.game.draftPool.clear();
        this.game.draftPool.setRange(
          selectedHouseCards.map((hc) => [hc.id, hc])
        );
        this.entireGame.broadcastToClients({
          type: "update-draft-pool",
          houseCards: this.game.draftPool.keys
        });
        this.participatingHouses.forEach((h) => h.houseCards.clear());
        this.parentGameState.assignRandomHouseCardsAndTracks();
        this.parentGameState.onDraftHouseCardsGameStateEnd();
      }
    }
  }

  onServerMessage(_: ServerMessage): void {}

  serializeToClient(
    _admin: boolean,
    _player: Player | null
  ): SerializedSelectedRandomDraftGameState {
    return {
      type: "selected-random-draft"
    };
  }

  static deserializeFromServer(
    draft: DraftGameState,
    _: SerializedSelectedRandomDraftGameState
  ): SelectedRandomDraftGameState {
    const selectedRandomDraftGameState = new SelectedRandomDraftGameState(
      draft
    );

    return selectedRandomDraftGameState;
  }
}

export interface SerializedSelectedRandomDraftGameState {
  type: "selected-random-draft";
}

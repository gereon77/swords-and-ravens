import { observer } from "mobx-react";
import { Component, ReactNode } from "react";
import GameStateComponentProps from "../GameStateComponentProps";
import renderChildGameState from "../../utils/renderChildGameState";
import React from "react";
import Col from "react-bootstrap/Col";
import DoranMartell1stAbilityGameState from "../../../common/ingame-game-state/action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/doran-martell-1st-ability-game-state/DoranMartell1stAbilityGameState";
import SimpleChoiceGameState from "../../../common/ingame-game-state/simple-choice-game-state/SimpleChoiceGameState";
import SimpleChoiceComponent from "../SimpleChoiceComponent";

@observer
export default class DoranMartell1stAbilityComponent extends Component<
  GameStateComponentProps<DoranMartell1stAbilityGameState>
> {
  render(): ReactNode {
    return (
      <>
        <Col xs={12} className="text-center">
          <b>Doran Martell:</b> House{" "}
          <b>{this.props.gameState.childGameState.house.name}</b> may choose to
          steal any dominance token held by House{" "}
          <b>{this.props.gameState.enemy.name}</b>.
        </Col>
        {renderChildGameState(this.props, [
          [SimpleChoiceGameState, SimpleChoiceComponent]
        ])}
      </>
    );
  }
}

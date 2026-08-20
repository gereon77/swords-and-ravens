import { observer } from "mobx-react";
import { Component, ReactNode } from "react";
import GameStateComponentProps from "../GameStateComponentProps";
import renderChildGameState from "../../utils/renderChildGameState";
import React from "react";
import Col from "react-bootstrap/Col";
import SimpleChoiceGameState from "../../../common/ingame-game-state/simple-choice-game-state/SimpleChoiceGameState";
import SimpleChoiceComponent from "../SimpleChoiceComponent";
import SelectOrdersComponent from "../SelectOrdersComponent";
import SelectOrdersGameState from "../../../common/ingame-game-state/select-orders-game-state/SelectOrdersGameState";
import AshaGreyjoy1stAbilityGameState from "../../../common/ingame-game-state/action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/asha-greyjoy-1st-ability-game-state/AshaGreyjoy1stAbilityGameState";

@observer
export default class AshaGreyjoy1stAbilityComponent extends Component<
  GameStateComponentProps<AshaGreyjoy1stAbilityGameState>
> {
  render(): ReactNode {
    return (
      <>
        <Col xs={12} className="text-center">
          <b>Asha Greyjoy:</b> House{" "}
          <b>{this.props.gameState.childGameState.house.name}</b> can choose to
          remove one Support or Consolidate Power order of House{" "}
          <b>
            {
              this.props.gameState.combat.getEnemy(
                this.props.gameState.childGameState.house
              ).name
            }
          </b>{" "}
          adjacent to <b>{this.props.gameState.combat.defendingRegion.name}</b>.
        </Col>
        {renderChildGameState(this.props, [
          [SimpleChoiceGameState, SimpleChoiceComponent],
          [SelectOrdersGameState, SelectOrdersComponent]
        ])}
      </>
    );
  }
}

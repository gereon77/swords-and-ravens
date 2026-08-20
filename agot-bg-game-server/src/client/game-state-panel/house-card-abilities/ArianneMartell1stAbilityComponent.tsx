import { observer } from "mobx-react";
import { Component, ReactNode } from "react";
import GameStateComponentProps from "../GameStateComponentProps";
import renderChildGameState from "../../utils/renderChildGameState";
import React from "react";
import Col from "react-bootstrap/Col";
import ArianneMartell1stAbilityGameState from "../../../common/ingame-game-state/action-game-state/resolve-march-order-game-state/combat-game-state/post-combat-game-state/after-winner-determination-game-state/arianne-martell-1st-ability-game-state/ArianneMartell1stAbilityGameState";
import SelectUnitsGameState from "../../../common/ingame-game-state/select-units-game-state/SelectUnitsGameState";
import SelectUnitsComponent from "../SelectUnitsComponent";

@observer
export default class ArianneMartell1stAbilityComponent extends Component<
  GameStateComponentProps<ArianneMartell1stAbilityGameState>
> {
  render(): ReactNode {
    return (
      <>
        <Col xs={12} className="text-center">
          <b>Arianne Martell</b>: House <b>{this.props.gameState.house.name}</b>{" "}
          may kill an army unit of House{" "}
          <b>
            {
              this.props.gameState.combat.getEnemy(this.props.gameState.house)
                .name
            }
          </b>
          .
        </Col>
        {renderChildGameState(this.props, [
          [SelectUnitsGameState, SelectUnitsComponent]
        ])}
      </>
    );
  }
}

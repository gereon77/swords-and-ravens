import { Component, default as React, ReactNode } from "react";
import { observer } from "mobx-react";
import preventOverflow from '@popperjs/core/lib/modifiers/preventOverflow';

import HouseCard from "../../../common/ingame-game-state/game-data-structure/house-card/HouseCard";
import houseCardImages from "../../houseCardImages";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import classNames = require("classnames");

interface HouseCardComponentProps {
    houseCard: HouseCard;
    size?: "small" | "medium" | "tiny";
    selected?: boolean;
    onClick?: () => void;
}

@observer
export default class HouseCardComponent extends Component<HouseCardComponentProps> {
    render(): ReactNode {
        return (
            <OverlayTrigger
                overlay={
                    <div className="vertical-game-card" style={{
                        backgroundImage: `url(${houseCardImages.get(this.props.houseCard.id)})`
                    }} />
                }
                popperConfig={{
                    modifiers: [preventOverflow]
                }}
                delay={{ show: 120, hide: 0 }}
                placement="auto"
            >
                <div
                    className={classNames(
                        "vertical-game-card hover-weak-outline",
                        this.props.size,
                        { "medium-outline hover-strong-outline": this.props.selected }
                    )}
                    style={{
                        backgroundImage: `url(${houseCardImages.get(this.props.houseCard.id)})`
                    }}
                    onClick={() => this.props.onClick ? this.props.onClick() : undefined}
                />
            </OverlayTrigger>
        );
    }
}

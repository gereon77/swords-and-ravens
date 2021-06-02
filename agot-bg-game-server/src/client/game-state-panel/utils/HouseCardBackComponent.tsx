import {Component, default as React, ReactNode} from "react";
import {observer} from "mobx-react";
import houseCardImages from "../../houseCardImages";
import classNames = require("classnames");
import House from "../../../common/ingame-game-state/game-data-structure/House";
import houseCardsBackImages, { houseCardsBackImagesDwd } from "../../houseCardsBackImages";
import HouseCard from "../../../common/ingame-game-state/game-data-structure/house-card/HouseCard";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";

interface HouseCardBackComponentProps {
    house: House;
    houseCard: HouseCard;
    size?: "small" | "medium" | "tiny";
    isDwd: boolean;
}

@observer
export default class HouseCardBackComponent extends Component<HouseCardBackComponentProps> {
    render(): ReactNode {
        return (
            <OverlayTrigger
                overlay={
                    <div className="vertical-game-card" style={{
                        backgroundImage: `url(${houseCardImages.get(this.props.houseCard.id)})`
                    }}/>
                }
                popperConfig={{modifiers: {preventOverflow: {boundariesElement: "viewport"}}}}
                delay={{show: 120, hide: 0}}
                placement="auto"
            >
                <div
                    className={classNames(
                        "vertical-game-card hover-weak-outline",
                        this.props.size
                    )}
                    style={{
                        backgroundImage: `url(${(
                            this.props.isDwd
                                ? houseCardsBackImagesDwd
                                : houseCardsBackImages
                            ).get(this.props.house.id)})`
                    }}
                />
            </OverlayTrigger>
        );
    }
}

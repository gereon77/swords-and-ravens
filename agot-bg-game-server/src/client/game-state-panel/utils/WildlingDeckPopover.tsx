import React, { ReactElement, ReactNode } from "react";
import Overlay, { OverlayInjectedProps } from "react-bootstrap/Overlay";
import Popover from "react-bootstrap/Popover";
import { t } from "../../i18n";
import GameLog from "../../../common/ingame-game-state/game-data-structure/GameLog";
import WildlingCard from "../../../common/ingame-game-state/game-data-structure/wildling-card/WildlingCard";
import WildlingCardType from "../../../common/ingame-game-state/game-data-structure/wildling-card/WildlingCardType";
import WildlingCardComponent from "./WildlingCardComponent";

interface WildlingDeckPopoverProps {
  wildlingDeck: WildlingCard[];
  logs: GameLog[];
  knowsWildlingCard: boolean;
  nextWildlingCard?: WildlingCardType;
  children: ReactElement<any>;
}

export default class WildlingDeckPopover extends React.Component<WildlingDeckPopoverProps> {
  state = { show: false };

  private triggerRef = React.createRef<HTMLElement>();
  private hideTimeout: ReturnType<typeof setTimeout> | null = null;

  componentWillUnmount(): void {
    this.clearHideTimeout();
  }

  render(): ReactNode {
    return (
      <>
        {React.cloneElement(this.props.children, {
          ref: this.triggerRef,
          onMouseEnter: this.showPopover,
          onMouseLeave: this.scheduleHide,
          onFocus: this.showPopover,
          onBlur: this.scheduleHide
        })}
        <Overlay
          target={() => this.triggerRef.current}
          show={this.state.show}
          placement="auto"
          rootClose
          onHide={this.scheduleHide}
        >
          {(overlayProps) => this.renderPopover(overlayProps)}
        </Overlay>
      </>
    );
  }

  private showPopover = (): void => {
    this.clearHideTimeout();
    if (!this.state.show) {
      this.setState({ show: true });
    }
  };

  private scheduleHide = (): void => {
    this.clearHideTimeout();
    this.hideTimeout = setTimeout(() => {
      this.hideTimeout = null;
      this.setState({ show: false });
    }, 350);
  };

  private clearHideTimeout(): void {
    if (this.hideTimeout !== null) {
      clearTimeout(this.hideTimeout);
      this.hideTimeout = null;
    }
  }

  private renderPopover = (overlayProps: OverlayInjectedProps): ReactNode => {
    const revealedWildlingCardIds = new Set<number>();
    for (const log of this.props.logs) {
      if (log.data.type === "wildling-card-revealed") {
        revealedWildlingCardIds.add(log.data.wildlingCard);
      }
    }

    const visibleWildlingCards = this.props.wildlingDeck.filter(
      (card) => card.type.id !== this.props.nextWildlingCard?.id
    );
    const usedWildlingCards = visibleWildlingCards
      .filter((card) => revealedWildlingCardIds.has(card.id))
      .sort((a, b) => a.type.name.localeCompare(b.type.name));
    const remainingWildlingCards = visibleWildlingCards
      .filter((card) => !revealedWildlingCardIds.has(card.id))
      .sort((a, b) => a.type.name.localeCompare(b.type.name));

    return (
      <Popover
        {...overlayProps}
        id="wildling-threat-tooltip"
        onMouseEnter={this.showPopover}
        onMouseLeave={this.scheduleHide}
      >
        <div className="row">
          {this.props.knowsWildlingCard && this.props.nextWildlingCard && (
            <>
              <div className="col-12 mt-0">
                <h5 className="text-center">
                  {t("gameStateColumn.topWildingCard5070eb")}
                </h5>
              </div>
              <div className="col-12 mb-2">
                <div className="d-flex justify-content-center">
                  <WildlingCardComponent
                    cardType={this.props.nextWildlingCard}
                    size="smedium"
                    tooltip
                  />
                </div>
              </div>
            </>
          )}
          <div className="col-12 mt-0">
            <h5 className="text-center">
              {t("gameStateColumn.usedWildlingCards3dcd43")}
            </h5>
          </div>
          <div className="col-12">
            <div className="row justify-content-center me-0 ms-0">
              {usedWildlingCards.map((card) => (
                <div
                  className="col-auto justify-content-center"
                  key={`used-wildling-${card.id}`}
                >
                  <WildlingCardComponent
                    cardType={card.type}
                    size="small"
                    tooltip
                  />
                </div>
              ))}
              {usedWildlingCards.length === 0 && (
                <div className="text-center text-muted">
                  {t("gameStateColumn.noWildlingCardsUsedYet93f013")}
                </div>
              )}
            </div>
          </div>
          <div className="col-12 mt-2">
            <h5 className="text-center">
              {t("gameStateColumn.remainingWildlingCardsc7dd4e")}
            </h5>
          </div>
          <div className="col-12">
            <div className="row justify-content-center me-0 ms-0">
              {remainingWildlingCards.map((card) => (
                <div
                  className="col-auto justify-content-center"
                  key={`remaining-wildling-${card.id}`}
                >
                  <WildlingCardComponent
                    cardType={card.type}
                    size="small"
                    tooltip
                  />
                </div>
              ))}
            </div>
          </div>
        </div>
      </Popover>
    );
  };
}

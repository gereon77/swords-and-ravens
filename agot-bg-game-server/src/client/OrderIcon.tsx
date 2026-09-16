import { Component, ReactNode, TransitionEvent } from "react";
import * as React from "react";
import classNames from "classnames";

interface OrderIconProps {
  // Image shown on the front face, before the flip completes (e.g. the hidden, house-colored
  // order back, or simply the revealed order when no flip is in progress).
  frontImage: string;
  // Image shown on the back face, revealed only once the flip passes the halfway point (e.g.
  // the actual, just-revealed order icon). When omitted, the icon is rendered flat, without any
  // 3D flip scene, so it doesn't pay the extra DOM/perspective cost for the common case.
  backImage?: string;
  className?: string;
  drawBorder?: boolean;
  borderColor?: string;
  // Called once the CSS 3D flip transition (or, for the flat fallback, a CSS keyframe
  // animation such as pulsate-bck) has fully finished.
  onAnimationEnd?: () => void;
}

interface OrderIconState {
  flipped: boolean;
}

/**
 * Renders a map order token. When `backImage` is provided, it performs a real 3D flip animation
 * (front/back faces with backface-visibility, rotated in a perspective scene) instead of the
 * previous approach of running a rotateY keyframe animation on a single flat icon while abruptly
 * swapping its background image mid-flight.
 */
export default class OrderIcon extends Component<
  OrderIconProps,
  OrderIconState
> {
  state: OrderIconState = { flipped: false };
  private raf1: number | null = null;
  private raf2: number | null = null;

  componentDidMount(): void {
    if (!this.props.backImage) {
      return;
    }

    // Mount in the un-flipped state first, then flip on a later frame so the browser always
    // has a "before" state to transition from. A single requestAnimationFrame is sometimes
    // batched together with the initial layout by the browser, so nest a second one to
    // reliably get a style recalculation before the "flipped" class is applied.
    this.raf1 = requestAnimationFrame(() => {
      this.raf2 = requestAnimationFrame(() => {
        this.setState({ flipped: true });
      });
    });
  }

  componentWillUnmount(): void {
    if (this.raf1 != null) {
      cancelAnimationFrame(this.raf1);
    }
    if (this.raf2 != null) {
      cancelAnimationFrame(this.raf2);
    }
  }

  onSceneAnimationEnd = (): void => {
    this.props.onAnimationEnd?.();
  };

  onFlipperTransitionEnd = (e: TransitionEvent<HTMLDivElement>): void => {
    if (e.propertyName == "transform") {
      this.props.onAnimationEnd?.();
    }
  };

  render(): ReactNode {
    const { frontImage, backImage, className, drawBorder, borderColor } =
      this.props;

    if (!backImage) {
      return (
        <div
          style={{ backgroundImage: `url(${frontImage})`, borderColor }}
          className={classNames("order-icon", className, {
            "order-border": drawBorder
          })}
          onAnimationEnd={this.onSceneAnimationEnd}
        />
      );
    }

    return (
      <div
        className={classNames("order-icon-scene", className)}
        onAnimationEnd={this.onSceneAnimationEnd}
      >
        <div
          className={classNames("order-icon-flipper", {
            flipped: this.state.flipped
          })}
          onTransitionEnd={this.onFlipperTransitionEnd}
        >
          <div
            style={{ backgroundImage: `url(${frontImage})`, borderColor }}
            className={classNames("order-icon", "order-icon-face", {
              "order-border": drawBorder
            })}
          />
          <div
            style={{ backgroundImage: `url(${backImage})`, borderColor }}
            className={classNames(
              "order-icon",
              "order-icon-face",
              "order-icon-face-back",
              { "order-border": drawBorder }
            )}
          />
        </div>
      </div>
    );
  }
}

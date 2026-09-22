import { AnimationEvent, Component, CSSProperties, ReactNode } from "react";
import * as React from "react";
import classNames from "classnames";

interface FlipIconProps {
  // Image shown on the front face, before the flip completes (e.g. the hidden order/house-card
  // back, or simply the revealed side when no flip is in progress).
  frontImage: string;
  // Image shown on the back face, revealed only once the flip passes the halfway point (e.g.
  // the actual, just-revealed order or house card). When omitted, the icon is rendered flat,
  // without any 3D flip scene, so it doesn't pay the extra DOM/perspective cost for the common,
  // non-animating case.
  backImage?: string;
  // Class(es) defining this element's box: size, background-size/position/repeat, and any other
  // static visual styling (e.g. "order-icon", or "vertical-game-card small"). Applied to the
  // flat fallback and to both faces, and also used (unadorned) to size the flip scene itself.
  boxClassName: string;
  // Extra classes layered on top of boxClassName (borders, highlight/attention animations, ...).
  className?: string;
  style?: CSSProperties;
  // Perspective distance (px) for the 3D flip scene. Should be only a few multiples of the
  // element's own size to give a strong sense of depth; defaults to a distance tuned for small,
  // icon/card-sized elements.
  perspective?: number;
  // Called once the CSS 3D flip animation (or, for the flat fallback, a CSS keyframe animation
  // such as pulsate-bck) has fully finished.
  onAnimationEnd?: () => void;
}

/**
 * Renders an icon/card that can perform a real 3D flip animation (front/back faces with
 * backface-visibility, rotated in a perspective scene) instead of running a rotateY keyframe
 * animation on a single flat element while abruptly swapping its background image mid-flight.
 * Used for both map order-reveal icons and combat house-card reveals.
 *
 * The flip itself is a plain CSS keyframe animation (see ".flip-flipper" in custom.scss) that
 * auto-plays as soon as the flipper element is mounted, rather than a JS-driven "un-flipped ->
 * flipped" transition. That makes it robust against the surrounding UI remounting this
 * component partway through the animation (e.g. CombatComponent remounts its whole subtree on
 * every combat-related server message) - a fresh mount just restarts the same self-contained
 * animation instead of getting stuck in an intermediate, JS-scheduled state.
 */
export default class FlipIcon extends Component<FlipIconProps> {
  onSceneAnimationEnd = (): void => {
    this.props.onAnimationEnd?.();
  };

  onFlipperAnimationEnd = (e: AnimationEvent<HTMLDivElement>): void => {
    // Ignore animationend events bubbling up from something other than the flipper itself.
    if (e.target === e.currentTarget) {
      this.props.onAnimationEnd?.();
    }
  };

  render(): ReactNode {
    const {
      frontImage,
      backImage,
      boxClassName,
      className,
      style,
      perspective = 150
    } = this.props;

    if (!backImage) {
      return (
        <div
          style={{ backgroundImage: `url(${frontImage})`, ...style }}
          className={classNames(boxClassName, className)}
          onAnimationEnd={this.onSceneAnimationEnd}
        />
      );
    }

    return (
      <div
        className={classNames("flip-scene", boxClassName, className)}
        style={{ perspective: `${perspective}px` }}
      >
        <div
          className="flip-flipper"
          onAnimationEnd={this.onFlipperAnimationEnd}
        >
          <div
            style={{ backgroundImage: `url(${frontImage})`, ...style }}
            className={classNames("flip-face", boxClassName, className)}
          />
          <div
            style={{ backgroundImage: `url(${backImage})`, ...style }}
            className={classNames(
              "flip-face",
              "flip-face-back",
              boxClassName,
              className
            )}
          />
        </div>
      </div>
    );
  }
}

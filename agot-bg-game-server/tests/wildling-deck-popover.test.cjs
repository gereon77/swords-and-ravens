require("./dom-setup.cjs");
const assert = require("node:assert/strict");
const { test, afterEach } = require("node:test");
const React = require("react");
const { createRoot } = require("react-dom/client");
require("../src/common/EntireGame");
const WildlingDeckPopover =
  require("../src/client/game-state-panel/utils/WildlingDeckPopover").default;
const WildlingCard =
  require("../src/common/ingame-game-state/game-data-structure/wildling-card/WildlingCard").default;
const wildlingCardTypes =
  require("../src/common/ingame-game-state/game-data-structure/wildling-card/wildlingCardTypes").default;

const mountedRoots = [];

afterEach(() => {
  for (const { root, container } of mountedRoots.splice(0)) {
    React.act(() => root.unmount());
    container.remove();
  }
});

function headingCards(headingName) {
  const title = heading(headingName);
  const section = title.parentElement.nextElementSibling;
  return [...section.querySelectorAll(".vertical-game-card.small")];
}

function renderDeck(props) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  mountedRoots.push({ root, container });
  React.act(() => {
    root.render(
      React.createElement(
        WildlingDeckPopover,
        props,
        React.createElement("button", null, "Wildlings deck")
      )
    );
  });
  return container;
}

async function hover(element) {
  await React.act(async () => {
    element.dispatchEvent(new window.MouseEvent("mouseover", { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 350));
  });
}

async function movePointer(from, to) {
  await React.act(async () => {
    from.dispatchEvent(
      new window.MouseEvent("mouseout", {
        bubbles: true,
        relatedTarget: to
      })
    );
    to.dispatchEvent(
      new window.MouseEvent("mouseover", {
        bubbles: true,
        relatedTarget: from
      })
    );
  });
}

function heading(text) {
  return [...document.body.querySelectorAll("h5")].find(
    (element) => element.textContent === text
  );
}

test("hovering the wildling deck separates revealed cards from remaining cards", async () => {
  const crowKillers = wildlingCardTypes.get("crow-killers");
  const mammothRiders = wildlingCardTypes.get("mammoth-riders");
  const silenceAtTheWall = wildlingCardTypes.get("silence-at-the-wall");
  const deck = [
    new WildlingCard(101, crowKillers),
    new WildlingCard(102, mammothRiders),
    new WildlingCard(103, silenceAtTheWall)
  ];
  const container = renderDeck({
    wildlingDeck: deck,
    logs: [
      {
        time: new Date(),
        data: { type: "wildling-card-revealed", wildlingCard: 102 }
      }
    ],
    knowsWildlingCard: true,
    nextWildlingCard: silenceAtTheWall
  });

  await hover(container.querySelector("button"));

  assert.ok(heading("Already Used"));
  assert.equal(headingCards("Already Used").length, 1);
  assert.match(
    headingCards("Already Used")[0].style.backgroundImage,
    /Mammothlarge\.png/
  );
  assert.equal(headingCards("Remaining Cards").length, 1);
  assert.match(
    headingCards("Remaining Cards")[0].style.backgroundImage,
    /CrowKillersLarge\.png/
  );
  assert.ok(heading("Top Wilding Card"));
  assert.equal(document.body.querySelectorAll(".vertical-game-card.smedium").length, 1);
});

test("the hovered preview reports an empty used section before the first reveal", async () => {
  const deck = [
    new WildlingCard(201, wildlingCardTypes.get("crow-killers")),
    new WildlingCard(202, wildlingCardTypes.get("mammoth-riders"))
  ];
  const container = renderDeck({ wildlingDeck: deck, logs: [], knowsWildlingCard: false });

  await hover(container.querySelector("button"));

  assert.ok(document.body.textContent.includes("No Wildling cards have been used yet."));
  assert.equal(headingCards("Already Used").length, 0);
  assert.equal(headingCards("Remaining Cards").length, 2);
});

test("moving the pointer from the deck onto its preview keeps it open for card inspection", async () => {
  const deck = [
    new WildlingCard(301, wildlingCardTypes.get("crow-killers")),
    new WildlingCard(302, wildlingCardTypes.get("mammoth-riders"))
  ];
  const container = renderDeck({
    wildlingDeck: deck,
    logs: [],
    knowsWildlingCard: false
  });
  const trigger = container.querySelector("button");

  await hover(trigger);
  const popover = document.getElementById("wildling-threat-tooltip");
  assert.ok(popover);

  await movePointer(trigger, popover);
  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 500));
  });

  assert.equal(document.getElementById("wildling-threat-tooltip"), popover);
  const card = popover.querySelector(".vertical-game-card.small");
  assert.ok(card);
  await hover(card);
  const cardPreview = document.body.querySelector(
    ".vertical-game-card.bring-to-front"
  );
  assert.ok(cardPreview);

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 500));
  });

  assert.equal(document.getElementById("wildling-threat-tooltip"), popover);
  assert.ok(document.body.contains(cardPreview));
});

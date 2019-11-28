import HouseCardAbility from "./HouseCardAbility";
import BetterMap from "../../../../utils/BetterMap";
import TheonGreyjoyHouseCardAbility from "./TheonGreyjoyHouseCardAbility";
import SerDavosSeaworthHouseCardAbility from "./SerDavosSeaworthHouseCardAbility";
import RenlyBaratheonHouseCardAbility from "./RenlyBaratheonHouseCardAbility";
import TywinLannisterHouseCardAbility from "./TywinLannisterHouseCardAbility";
import SalladhorSaanHouseCardAbility from "./SalladhorSaanHouseCardAbility";
import AshaGreyjoyHouseCardAbilities from "./AshaGreyjoyHouseCardAbilities";
import QueenOfThornsHouseCardAbility from "./QueenOfThornsHouseCardAbility";
import VictarionGreyjoyHouseCardAbility from "./VictarionGreyjoyHouseCardAbility";
import BalonGreyjoyHouseCardAbility from "./BalonGreyjoyHouseCardAbility";
import DoranMartellHouseCardAbility from "./DoranMartellHouseCardAbility";
import PatchfaceHouseCardAbility from "./PatchfaceHouseCardAbility";

export const theonGreyjoy = new TheonGreyjoyHouseCardAbility(
    "theon-greyjoy",
    "If you are defending an area that contains either a Stronghold or a Castle,"
    + " this card gains +1 combat strength and a sword icon."
);
export const serDavosSeaworth = new SerDavosSeaworthHouseCardAbility(
    "ser-davos-seaworth",
    "If your \"Stannis Baratheon\", House card is in your discard pile, this card"
    + " gains +1 combat strength and a sword icon."
);
export const renlyBaratheon = new RenlyBaratheonHouseCardAbility(
    "renly-baratheon",
    "If you win this combat, you may upgrade one of your participating Footmen" +
    + " (or one supporting Baratheon Footmen) to a Knight."
);
export const tywinLannister = new TywinLannisterHouseCardAbility(
    "tywin-lannister",
    "If you win this combat, gain two Power tokens."
);
export const salladhorSaan = new SalladhorSaanHouseCardAbility(
    "salladhor-saan",
    "If you are being supported in this combat, the combat strength of all"
    + "non-Baratheon ships is reduced to zero."
);
export const ashaGreyjoy = new AshaGreyjoyHouseCardAbilities(
    "asha-greyjoy",
    "If you are not being supported in this combat, this card gains"
    + " two sword icons and one fortification icon."
);
export const queenOfThorns = new QueenOfThornsHouseCardAbility(
    "queen-of-thorns",
    "Immediately remove one of your opponent's Order tokens in any one area"
    + " adjacent to the embattled area. You may not remove the March Order token"
    + " used to start this combat."
);
export const victarionGreyjoy = new VictarionGreyjoyHouseCardAbility(
    "victarion-greyjoy",
    "If you are attacking, all of you participating Ships (including"
     + " supporting Greyjoy Ships) add +2 to combat strength instead of +1."
);
export const balonGreyjoy = new BalonGreyjoyHouseCardAbility(
    "balon-greyjoy",
    "The printed combat strength of your opponent's House card is reduced to 0."
);
export const doranMartell = new DoranMartellHouseCardAbility(
    "doran-martell",
    "Immediately move your opponent to the bottom of one Influence track of your choice."
);
export const patchface = new PatchfaceHouseCardAbility(
    "patchface",
    "After combat, you may look at your opponent's hand and discard one card of your choice."
);

const houseCardAbilities = new BetterMap<string, HouseCardAbility>([
    [theonGreyjoy.id, theonGreyjoy],
    [serDavosSeaworth.id, serDavosSeaworth],
    [renlyBaratheon.id, renlyBaratheon],
    [tywinLannister.id, tywinLannister],
    [salladhorSaan.id, salladhorSaan],
    [ashaGreyjoy.id, ashaGreyjoy],
    [queenOfThorns.id, queenOfThorns],
    [victarionGreyjoy.id, victarionGreyjoy],
    [balonGreyjoy.id, balonGreyjoy],
    [doranMartell.id, doranMartell],
    [patchface.id, patchface],
]);

export default houseCardAbilities;

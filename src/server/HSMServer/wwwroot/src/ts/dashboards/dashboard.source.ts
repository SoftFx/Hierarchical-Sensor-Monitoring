import {Plot} from "../../js/plots";

export class Source {
    //plot data
    id: string;
    plot: Plot;
    constructor(id: string, plot: Plot) {
        this.id = id;
        this.plot = plot;
    }
}

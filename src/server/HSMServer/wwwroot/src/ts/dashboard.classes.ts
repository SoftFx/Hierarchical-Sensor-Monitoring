import {Plot} from "../js/plots";

export class PlotUpdate {
    x: any[] = [];
    y: any[] = [];
    customdata: any[] = [];
}

export class Redraw {
    traces: Plot[] = [];
    traceIds: number[] = [];

    add(trace: Plot, id: number) {
        this.traces.push(trace);
        this.traceIds.push(id);
    }
}
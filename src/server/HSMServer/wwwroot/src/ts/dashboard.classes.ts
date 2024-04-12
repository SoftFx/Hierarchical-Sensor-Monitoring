import {Plot} from "../js/plots";
import {Hovermode} from "./types";
import {IPanelSettings} from "./dashboard.interfaces";

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


export class PanelSettings {
    id: string
    
    hovermode: Hovermode
    hoverDistance: number
    
    showLegend: boolean
    
    width: number
    height: number
    x: number
    y:number

    constructor(id: string, settings: IPanelSettings) {
        this.id = id;
        
        this.hovermode = settings.hovermode;
        this.hoverDistance = settings.hoverDistance;
        
        this.showLegend = settings.showLegend;
        
        this.width = settings.width;
        this.height = settings.height;
        this.x = settings.x;
        this.y = settings.y;
    }
}
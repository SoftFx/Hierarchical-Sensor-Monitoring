import {Plot} from "../js/plots";
import {HoverModeEnum} from "./types";
import {IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";

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
    
    hovermode: HoverModeEnum
    hoverDistance: number
    
    showLegend: boolean
    isSingleMode: boolean

    width: number
    height: number
    x: number
    y:number

    autoscale: boolean
    range: boolean | [number, number]

    constructor(id: string, settings: IPanelSettings, ySettings: IYRangeSettings) {
        this.id = id;
        
        this.hovermode = settings.hovermode;
        this.hoverDistance = 20;
        
        this.showLegend = settings.showLegend;
        this.isSingleMode = settings.isSingleMode;
        
        this.width = settings.width;
        this.height = settings.height;
        this.x = settings.x;
        this.y = settings.y;
        
        this.autoscale = ySettings.autoScale;
        this.range = this.autoscale === true ? true : [Number(ySettings.minValue), Number(ySettings.maxValue)]
    }
}
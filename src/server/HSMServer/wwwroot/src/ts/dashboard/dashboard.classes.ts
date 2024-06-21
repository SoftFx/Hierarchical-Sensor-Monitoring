
import {IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";
import {Plot} from "../../js/plots";
import {HoverModeEnum} from "../types";

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

    private _width: number
    private _height: number

    private _singleModeWidth: number
    private _sHeight: number
    
    x: number
    y:number

    autoscale: boolean
    range: boolean | [number, number]

    public get width(){
        return this.isSingleMode ? 
            this._singleModeWidth :
            this._width;
    }

    public get height(){
        return this.isSingleMode ?
            this._sHeight :
            this._height;
    }
    
    constructor(id: string, settings: IPanelSettings, ySettings: IYRangeSettings) {
        this.id = id;
        
        this.hovermode = settings.hovermode;
        this.hoverDistance = 20;
        
        this.showLegend = settings.showLegend;
        this.isSingleMode = settings.isSingleMode;
        
        this._width = settings.width;
        this._height = settings.height;
        
        this._sHeight = settings.sHeight;
        this._singleModeWidth = settings.singleModeWidth;
        
        this.x = settings.x;
        this.y = settings.y;
        
        this.autoscale = ySettings.autoScale;
        this.range = this.autoscale === true ? true : [Number(ySettings.minValue), Number(ySettings.maxValue)]
    }
    
    
}
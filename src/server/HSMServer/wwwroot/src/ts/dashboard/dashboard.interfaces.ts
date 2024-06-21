import {Datum} from "plotly.js";
import {HoverModeEnum} from "../types";

export interface Dictionary<T> {
    [Key: string]: T;
}

export interface ISourceUpdate {
    id: string,
    update: {
        newVisibleValues: Array<{
            value: number | string,
            id: number,
            time: string,
            tooltip: string
        }>,
        isTimeSpan: boolean
    }
}

export interface IPanelSettings {
    hovermode: HoverModeEnum
    hoverDistance: number

    showLegend: boolean
    isSingleMode: boolean

    width: number
    height: number

    singleModeWidth: number
    sHeight: number
    
    x: number
    y: number
}

export interface IYRangeSettings {
    autoScale: boolean
    maxValue: number,
    minValue: number
}

export interface ISourceSettings{
    id: string,
    panelId: string,
    dashboardId: string,
    chartType: number,
    color: string,
    label: string,
    shape: string,

    values: IValue[]
    range: [Datum, Datum] | boolean,
    sensorInfo: ISensorInfo
}

interface ISensorInfo{
    realType: number,
    plotType: number,
    units: string
}

interface IValue{
    time: Date,
    tooltip: string,
    value: any
}
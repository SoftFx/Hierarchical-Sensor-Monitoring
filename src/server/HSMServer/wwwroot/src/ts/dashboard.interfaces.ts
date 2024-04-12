import {Hovermode} from "./types";

export interface Dictionary<T> {
    [Key: string]: T;
}

export interface IPanel {
    id: string,
    sources: ISource[],
    requestTimeout: number,
    range: boolean | [number, number],
    isTimeSpan: boolean
}

export interface ISource {
    id: string,
    range: boolean | [number, number]
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
    hovermode: Hovermode
    hoverDistance: number

    showLegend: boolean

    width: number
    height: number
    x: number
    y:number
}
export interface Panel {
    id: string,
    sources: Source[],
    requestTimeout: number,
    range: boolean | [number, number],
    isTimeSpan: boolean
}

export interface Source {
    id: string,
    range: boolean | [number, number]
}

export interface SourceUpdate {
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
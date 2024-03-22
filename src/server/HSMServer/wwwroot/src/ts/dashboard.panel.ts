import {Source} from "./dashboard.source";

export class Panel {
    //sources
    sources: Source[] = [];

    id: string;
    divId: string;
    settings: Settings;
    rangeSettings: RangeSettings;

    public constructor(id: string, settings: Settings, rangeSettings: RangeSettings) {
        this.id = id;
        this.divId = `panelChart_panelChart_${this.id}`;
        this.settings = settings;
        this.rangeSettings = rangeSettings;
    }

    public addSource(source: Source) {
        this.sources.push(source)
    }
}

interface Settings {
    width: number;
    height: number;
    x: number;
    y: number;
    showLegend: boolean;
}

interface RangeSettings {
    autoScale: boolean;
    maxValue: number;
    minValue: number;
}

import {Layout} from "plotly.js";

export interface Value {
    id: number,
    time: Date,
    tooltip: string,
    value: number
}
interface SensorInfo{
    plotType: number,
    realType: number,
    units: string
}

export interface Source {
    id: string,
    color: string,
    shape: string,
    values: Value[],
    sensorInfo: SensorInfo
}

export class PlotTest {
    connectgaps: boolean = false;
    
    x: Date[] = [];
    y: number[] = [];
    customdata: string[] = [];
    
    type: string;
    mode: string;
    
    layout: Layout;
    
    public constructor(source: Source) {
        console.log(source.sensorInfo);
        console.log(source.values);

        this.parseData(source.values);
    }
    
    protected parseData(values: Value[]){
        for (let i of values){
            this.x.push(i.time)
            this.y.push(i.value);
            this.addCustomData(i)
        }
    }
    
    protected addCustomData(value: Value){
        console.log('test default')
    }
}
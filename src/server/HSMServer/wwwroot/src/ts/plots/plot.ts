import {Layout, ScatterData, ScatterLine} from "plotly.js";
import {Colors} from "../../js/plots";
import {ISourceSettings} from "../dashboard/dashboard.interfaces";

export class Plot<T> {
    data: ScatterData;
    layout: Layout;
    
    type : string;
    mode: string;
    hovertemplate = "%{x}, %{customdata}<extra></extra>";
    line = {
        color: Colors.defaultTrace,
        shape: ""
    }

    showlegend = false;
    autoscaleY = true;
    
    marker = {
        color: Array<string>,
        size: Array<number>,
        opacity: 1,
        line: {
            color: Colors.line,
            width: 0
        }
    };
    
    x: Date[] = [];
    y: T[] = [];
    customdata: string[] = [];
    
    constructor(settings: Partial<ISourceSettings>) {
        this.line.color = settings.color;
        this.line.shape = settings.shape;
    }

    getPlotData() {
        return [this];
    }
    
    getLayout(){
        return {
            dragmode: 'zoom',
            autosize: true,
            xaxis: {
                type: 'date',
                autorange: true,
                title: {
                    //text: 'Time',
                    font: {
                        family: 'Courier New, monospace',
                        size: 18,
                        color: '#7f7f7f'
                    }
                },
                rangeslider: {
                    visible: false
                }
            }
        }
    }
}
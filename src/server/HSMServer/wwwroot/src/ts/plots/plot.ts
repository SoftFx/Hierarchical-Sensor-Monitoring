import {Layout, ScatterData} from "plotly.js";
import {Colors} from "../../js/plots";

export class Plot<T> {
    data: ScatterData;
    layout: Layout;
    
    protected type : string;
    protected mode: string;

    showlegend = false;
    protected hovertemplate = "%{x}, %{customdata}<extra></extra>";
    customColor = Colors.default;
    autoscaleY = true;
    
    marker = {
        // @ts-ignore
        color: [],
        // @ts-ignore
        size: [],
        opacity: 1,
        line: {
            color: Colors.line,
            width: 0
        }
    };
    
    x: Date[] = [];
    y: T[] = [];
    customdata: string[] = [];
    
    constructor() {
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
import {Plot} from "./plot";
import {IVersionEntity, IVersionValue} from "../entities/version-entity";
import {Data, Layout} from "plotly.js";

export class VersionPlot extends Plot<string>{
    override type = 'scatter';
    override mode = 'lines+markers';
    hovertemplate = "%{customdata}<extra></extra>";
    
    constructor(values: IVersionValue[], color:string, shape:string) {
        super({
            color: color,
            shape: shape
        });

        for (let i of values) {
            this.x.push(i.time);
            this.y.push(this.getY(i.value));
            this.customdata.push(i.tooltip);
        }
    }

    getY(value: IVersionEntity) : string {
        let stringRepresentation = "";

        tryBuild(value.major, true)
        tryBuild(value.minor)
        tryBuild(value.build)
        tryBuild(value.revision)
        tryBuild(value.majorRevision)
        tryBuild(value.minorRevision)
        
        function tryBuild(value: number, q: boolean = false){
            if (value !== -1)
                stringRepresentation += q === true ? `${value}` : `.${value}`;
            else 
                stringRepresentation += '.-1';
        }
        
        return stringRepresentation;
    }
    
    override getLayout(y: string[]): Partial<Layout>{
        const layoutVals : string[] = [];
        const layoutText: string[] = [];
        
        
        for (const yVal of y) {
            layoutText.push(yVal.replaceAll('.-1', ''));
            layoutVals.push(yVal);
        }
        
       return {
            ...super.getLayout(),
           yaxis: {
               tickmode: "array",
               ticktext: layoutText,
               tickvals: layoutVals,
               tickfont: {
                   size: 10
               },
               // @ts-ignore
               automargin: "width+height"
           },
           autosize: true
       }
    }
    
    static getPanelLayout(data: Data[]){
        const layoutVals : string[] = [];
        const layoutText: string[] = [];
        const y: string[] = []
        
        for (const val of data) {
            //@ts-ignore
            y.push(...val.y);
        }
        
        for (const yVal of y) {
            if (yVal === null)
                continue;
            
            layoutText.push(yVal.replaceAll('.-1', ''));
            layoutVals.push(yVal);
        }

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
            },
            yaxis: {
                tickmode: "array",
                ticktext: layoutText,
                tickvals: layoutVals,
                tickfont: {
                    size: 10
                },
                // @ts-ignore
                automargin: "width+height"
            },
        }
    }
}
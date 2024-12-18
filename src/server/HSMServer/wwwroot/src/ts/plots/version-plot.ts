import {Plot} from "./plot";
import {IVersionEntity, IVersionValue} from "../entities/version-entity";
import {Data, Layout} from "plotly.js";
import {Dictionary} from "../dashboard/dashboard.interfaces";

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

    getY(value: IVersionEntity): string {
        let stringRepresentation = "";

        //VersionExtensions: version.Revision == 0 ? version.ToString(3) : version.ToString();

        if (value.revision == 0) {
            tryBuild(value.major, true)
            tryBuild(value.minor)
            tryBuild(value.build)
        }
        else {
            tryBuild(value.major, true)
            tryBuild(value.minor)
            tryBuild(value.build)
            tryBuild(value.revision)
            tryBuild(value.majorRevision)
            tryBuild(value.minorRevision)
        }

        function tryBuild(value: number, q: boolean = false) {
            if (value < 0)
                stringRepresentation += '.-1';
            else
                stringRepresentation += q === true ? `${value}` : `.${value}`;
        }

        return stringRepresentation;
    }
    
    getLayout(): Partial<Layout>{
        const layoutVals : string[] = [];
        const layoutText: string[] = [];
        
        for (const yVal of this.y) {
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
               categoryorder: 'category ascending',
               // @ts-ignore
               automargin: "width+height"
           },
           autosize: true
       }
    }
    
    static getYaxisTicks(data: Data[]){
        const layoutVals : string[] = [];
        const layoutText: string[] = [];
        const y: string[] = []

        let map: Dictionary<string> = {};
        for (const val of data) {
            //@ts-ignore
            y.push(...val.y);
        }

        for (const yVal of y) {
            if (yVal === null)
                continue;

            map[yVal] = yVal;
        }

        for (const yVal of Object.keys(map)) {
            layoutVals.push(yVal);
            layoutText.push(yVal.replaceAll('.-1', ''));
        }

        return {
                ticktext: layoutText,
                tickvals: layoutVals,
                categoryorder: 'category ascending',
            }
    }
}
import {Plot} from "./plot";
import {VersionEntity, VersionValue} from "../entities/version-entity";

export class VersionPlot extends Plot<string>{
    override type = 'scatter';
    override mode = 'lines+markers';
    override hovertemplate = "%{customdata}<extra></extra>";
    
    constructor(values: VersionValue[]) {
        super();
        // let test:string[] = [];
        // values.forEach(x => {
        //     test.push(this.getY(x.value));
        // })

        for (let i of values) {
            this.x.push(i.time);
            this.y.push(this.getY(i.value));
            this.customdata.push(i.tooltip);
        }
    }
    

    // compareVersion(value: VersionEntity): number {
    //     return value === null ? 1 :
    //         this.value.major != value.major ? (this.value.major > value.major ? 1 : -1) :
    //             this.value.minor != value.minor ? (this.value.minor > value.minor ? 1 : -1) :
    //                 this.value.build != value.build ? (this.value.build > value.build ? 1 : -1) :
    //                     this.value.revision != value.revision ? (this.value.revision > value.revision ? 1 : -1) :
    //                         0
    // }

    getY(value: VersionEntity) : string {
        return `${value.major}.${value.minor}${value.build}${value.revision}${value.majorRevision}${value.minorRevision}`;
    }
}
import {Plot} from "./plot";

export class NumberPlot extends Plot<string> {
    override type = 'scatter';
    override mode = 'lines+markers';
    
    constructor() {
        super();
    }
}
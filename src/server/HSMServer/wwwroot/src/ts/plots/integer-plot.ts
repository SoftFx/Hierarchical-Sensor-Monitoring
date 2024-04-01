import {PlotTest, Source} from "./plot";

class IntegerPlot extends PlotTest{
    public constructor(source: Source) {
        super(source);
        
        this.type = 'scatter';
        this.mode = 'lines+markers';
        
        
    }
}
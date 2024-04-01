import {PlotTest, Source, Value} from "./plot";

class BoolPlot extends PlotTest{
    public constructor(source: Source) {
        super(source);
        
        this.type = 'scatter';
        this.mode = 'markers';
        
    }
    
    override parseData(values: Value[]){
        super.parseData(values);
    }
}
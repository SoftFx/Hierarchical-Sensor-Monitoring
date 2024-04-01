import {PlotTest, Source, Value} from "./plot";

export class DoublePlotTest extends PlotTest {
    public constructor(source: Source) {
        super(source);

        console.log('constructor dp')
        this.type = 'scatter';
    }

    override addCustomData(value: Value) {
        console.log('double plot custom data')
        if (value.tooltip !== undefined && value.tooltip !== null) {
            this.customdata.push(value.value + '<br>' + value.tooltip);
            return;
        }

        this.customdata.push(`${value.value}`);
    }
}
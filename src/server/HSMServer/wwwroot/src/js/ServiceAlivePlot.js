import {Colors, MarkerSize, Plot} from "./plots";

export class ServiceAlivePlot extends Plot{
    constructor(data) {
        super();
        
        this.data = data;
        this.x = [];
        this.y = [];

        this.type = 'scattergl';
        this.mode = 'markers';
        this.marker = {
            color: [],
            size: [],
            opacity: 1,
            line: {
                color: Colors.line,
                width: 0
            }
        };
        
        this.setUpData(data);
    }
    
    setUpData(data) {
        for (let i of JSON.parse(data)) {
            if (i.value === false) {
                this.x.push(i.time)

                this.y.push(0.5);

                this.marker.color.push(Colors.red);
                this.marker.size.push(MarkerSize.Error)
            }
        }
    }
}
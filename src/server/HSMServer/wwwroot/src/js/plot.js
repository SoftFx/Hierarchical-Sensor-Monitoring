export class Plot {

    constructor(data) {
        this.basicInit();
    }
    basicInit (){
        this.x = [];
        this.y = [];
        this.customdata = [];
        this.type = '';
        this.mode = '';
        this.customdata = [];
        this.hovertemplate = '';
    }
    
    setUpData(){}
    
    getPlotData (){
        console.log([this])
        return [this];
    }
}

export class BoolPlot extends Plot {
    constructor(data) {
        super();
        this.type = 'scatter';
        this.mode = 'markers';
        this.marker = {
            color: [],
            size: 10
        }
        this.color = [];
        this.showlegend = false;

        this.setUpData([...new Map(data.map(item => [item['time'], item])).values()]);
    }

    setUpData(data) {
        for(let i of data){
            this.x.push(i.time)
            
            if (i.isTimeout === true){
                this.y.push(-1)
            }
            else {
                this.y.push(i.value === true ? 1 : 0)
                this.customdata.push(i.value === true);
                this.marker.color.push(i.value === true ? 'rgb(0,0,255)' : 'rgb(255,0,0)');
            }
        }
        
        this.hovertemplate = "%{x}, %{customdata}" +
                             "<extra></extra>";
    }
}

export class IntegerPlot extends Plot {
    constructor(data) {
        super();
        
        this.type = 'scatter';
        this.mode = 'lines+markers';
        this.line = {
            shape: 'hv'
        }
        
        this.setUpData(data);
    }

    setUpData(data) {
        for(let i of data){
            this.x.push(i.time)
            this.y.push(i.value)
        }
    }
}
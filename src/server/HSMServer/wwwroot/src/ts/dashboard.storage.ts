import {Dictionary} from "./dashboard.interfaces";
import * as moment from "moment";

export class DashboardStorage {
    private _intervalId: number;
    private panels: Dictionary<Panel> = {};

    id: string;

    public constructor() {
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public addPanel(id: string) {
        this.panels[id] = new Panel(id);
        
        window.clearInterval(this._intervalId)
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public getPanel(id: string): Panel {
        if (this.panels[id] === undefined)
            this.panels[id] = new Panel(id);

        return this.panels[id];
    }
    
    checkForUpdate(panels: Dictionary<Panel>) {
        return window.setInterval(function () {
            Object.values(panels).forEach(panel => {
                panel.updateNotify();
            });
        }, 5000);
    }
}

class Panel {
    private _lastUpdateTime: Date = new Date();
    private _lastUpdateDiv: JQuery<HTMLElement>;

    id: string

    constructor(id: string) {
        this.id = id;
        this._lastUpdateDiv = $('#lastUpdate_' + this.id);
    }

    get lastUpdateTime(): Date {
        return this._lastUpdateTime;
    }

    set lastUpdateTime(time: Date) {
        if (time > this._lastUpdateTime) {
            this._lastUpdateTime = time;
            console.log(this)
            this.updateNotify();
        }
    }

    updateNotify() {
        this._lastUpdateDiv.html(moment(this._lastUpdateTime).fromNow());
    }
}
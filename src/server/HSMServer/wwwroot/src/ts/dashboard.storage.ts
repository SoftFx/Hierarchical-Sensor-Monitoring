import {Dictionary} from "./dashboard.interfaces";
import * as moment from "moment";
import {Helper} from "./localStorage.helper";
import {Hovermode} from "./types";
import {Layout} from "./plotUpdate";

export class DashboardStorage {
    private _intervalId: number;
    private panels: Dictionary<Panel> = {};

    id: string;

    public constructor() {
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public addPanel(id: string) {
        this.panels[id] = new Panel(id);
        this.panels[id].updateNotify();

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

    private _savebutton: JQuery<HTMLElement>;
    
    
    id: string
    settings: PanelSettings

    
    
    
    constructor(id: string) {
        this.id = id;
        this._lastUpdateDiv = $('#lastUpdate_' + this.id);
        
        this.settings = Helper.read<PanelSettings>('panel_' + id);
        if (this.settings !== null) {
            $('#selecthovermode_' + id).val(this.settings.hovermode as string);
            $('#hoverdistance_' + id).val(this.settings.hoverdistance);
        }
        Layout.relayout(this.id, this.settings);
        
        this._savebutton = $('#button_save_settings_' + id);
        this._savebutton.on('click', function (){
            this.settings = new PanelSettings($('#selecthovermode_' + id).val() as Hovermode, $('#hoverdistance_' + id).val() as number);
            Helper.save("panel_" + this.id, this.settings);
            Layout.relayout(this.id, this.settings);
            $('#actionButton').trigger('click')
        }.bind(this))
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

export class PanelSettings {
    hovermode: Hovermode
    hoverdistance: number

    constructor(hovermode: Hovermode, hoverdistance: number) {
        this.hovermode = hovermode;
        this.hoverdistance = hoverdistance;
    }

}
import $ from 'jquery';

import * as bootstrap from 'bootstrap'
window.bootstrap = bootstrap;

import 'bootstrap/dist/css/bootstrap.min.css';

window.ClipboardJS = require('clipboard');
bootstrap.Toast.Default.delay = 3000;

import * as moment from 'moment';
window.moment = moment;

import 'jstree/dist/jstree.min.js';
import 'jstree/dist/themes/default/style.min.css';

import * as Plotly from 'plotly.js/dist/plotly';
window.Plotly = Plotly;

import '@fortawesome/fontawesome-free/js/all.min.js';
import '@fortawesome/fontawesome-free/css/all.min.css';


import './js/accessKey.js';
import './js/deletionConfirmation.js';
import './js/file.js';
import './js/history.js';
import './js/plotting.js';
import './js/sensorInfo.js';
import './js/tree.js';
import './js/site.js';

import './css/site.css';
import './css/accessKey.css';
import './css/home.css';
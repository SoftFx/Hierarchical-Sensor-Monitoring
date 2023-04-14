import $ from 'jquery';

import * as bootstrap from 'bootstrap';
window.bootstrap = bootstrap;
bootstrap.Toast.Default.delay = 3000;

import 'bootstrap/dist/css/bootstrap.min.css';

import 'bootstrap-select';
import 'bootstrap-select/dist/css/bootstrap-select.css';

window.ClipboardJS = require('clipboard');

import * as moment from 'moment';
window.moment = moment;

import 'jstree/dist/jstree.min.js';
import 'jstree/dist/themes/default/style.min.css';

import * as Plotly from 'plotly.js/dist/plotly';
window.Plotly = Plotly;

import * as TimeSpan from 'timespan';
window.TimeSpan = TimeSpan;

import * as Heiho from '@kktsvetkov/heiho';
window.Heiho = Heiho;

import * as Marked from 'marked'
window.marked = Marked;

import * as DOMPurify from 'dompurify';
window.DOMPurify = DOMPurify;

import '@kktsvetkov/heiho/heiho.css';

import 'datatables';
import 'datatables/media/css/jquery.dataTables.min.css';

import '@fortawesome/fontawesome-free/js/all.min.js';
import '@fortawesome/fontawesome-free/css/all.min.css';


import './js/accessKey.js';
import './js/deletionConfirmation.js';
import './js/file.js';
import './js/history.js';
import './js/plotting.js';
import './js/sensorInfo.js';
import './js/products.js';
import './js/tree.js';
import './js/site.js';

import './css/site.css';
import './css/accessKey.css';
import './css/home.css';
import './css/product.css';
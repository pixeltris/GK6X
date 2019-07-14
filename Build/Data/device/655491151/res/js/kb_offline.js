var CMS = {};
var DEVICE = {
  profiles: [],
  modeIndex: 2,
  profile: null,
  playle: null,
  keymap: null,
  leData: null,
  params: null,
  definecolor: "0xff0000"
};
var MENU = {
  "index": null,
  "menuPID":"",
  "menuID":"",
  "menuName":"",
  "keyLE": "",
  "driverValue":""
};
var hoverTimer;

$.multilang=window.parent.$.multilang;
function initUI() {
  initDeviceImage();
  
  if (CMS.currentProfile && (CMS.currentProfile.ModelID === CMS.deviceID)) 
    DEVICE.modeIndex = CMS.currentProfile.ModeIndex;

  for(var i = 0; i < CMS.profiles.length; i++) {
    var modeIndex = CMS.profiles[i].ModeIndex; 
    if(modeIndex == 1) {
      (function(modeIndex) {
        window.parent.readProfile(CMS.deviceID, CMS.profiles[i].GUID, function(data) {
          DEVICE.profiles[modeIndex] = data;
          if (modeIndex == DEVICE.modeIndex){
            onProfileSelect(DEVICE.modeIndex);
          }
        });
      })(modeIndex);
      break;
    }
  }
  for(var i = 0; i < CMS.profiles.length; i++) {
    var modeIndex = CMS.profiles[i].ModeIndex; 
    if((modeIndex >= 2) && (modeIndex <= 4)) {
      (function(modeIndex) {
        window.parent.readProfile(CMS.deviceID, CMS.profiles[i].GUID, function(data) {
          DEVICE.profiles[modeIndex] = data;
          if (modeIndex == DEVICE.modeIndex){
            onProfileSelect(DEVICE.modeIndex);
          }
        });
      })(modeIndex);
    }
  }
  if (window.parent.CMS.deviceConfig.AspectRatio) {
    $('#device').device({
      aspectratio: window.parent.CMS.deviceConfig.AspectRatio,
    });
  }
 
  $.le({
    onDisplay: function(data) {
      $('#device').device({"display": data});
    }
  });

  window.parent.setLanguage(false);
}


function initDeviceImage() {
  $("#device").empty();
  var str = '';
  str +=  '<img src="imgshow/device_outline.png" class="device-outline" />\
  <img src="imgshow/device_panel.png" class="device-panel" />\
  <img src="imgshow/device_keycap.png" class="device-keycap" />';
  $("#device").append(str);
}

$(document).ready(function() {
  CMS = window.parent.CMS;
  $.getJSON("data/keymapshow.js", function(json){
    DEVICE.keymap = json;
    initUI();
  });
});

function onProfileSelect(modeIndex) {
  DEVICE.modeIndex = modeIndex;
  DEVICE.profile = DEVICE.profiles[DEVICE.modeIndex];
  onProfileLoad();
  window.parent.changeMode(CMS.deviceID, modeIndex);
}

function onProfileLoad() {
  window.parent.CMS.currentProfile = DEVICE.profile;
  initDevice();
  initFunc();
  initFuncEvent();
}

function onProfileChanged() {
  window.parent.writeProfile(CMS.deviceID, DEVICE.profile.GUID, DEVICE.profile, function() {
    var isLECheckResult = checkLE();
    if(!isLECheckResult) {
      window.parent.warning($.multilang("not_found_light_file"));
      return;
    }
    window.parent.apply(CMS.deviceID, DEVICE.profile.GUID, function(result) {
      $('#apply').removeClass("btn-breath");
      if(result)
        window.parent.success($.multilang("apply_success"));
      else
        window.parent.error($.multilang("apply_error"));
    });
  });
}

function initDevice() {
  //初始化按键
  $('#device').device({ 
    keymap: DEVICE.keymap, 
  });

}

function initFunc() {
  //初始化左侧选择键以及默认功能菜单索引
  MENU.index = null;

  //初始化菜单栏选项
  $("#menu_select").find("li").eq(0).addClass("active");
  $("#lamp_setting").is(":hidden") && $("#func_setting").show();

  //初始化模式灯光功能按钮
  $("#tools_lamp_1").find(".ximagebutton").ximagebutton({
    colors: {
      normal: "#787878",
      active: "#00c2ff"
    }
  });
  $("#tools_lamp_1").find(".ximagebutton").eq(0).ximagebutton('active');
  $("#menu_select").find("li").unbind('click').click(function() {
    $(this).addClass("active").siblings().removeClass("active");
    
    $("#lamp_setting").is(":hidden") && $("#lamp_setting").show();
    $("#bottom_nav").css({'display': 'flex'});
    $("#apply").css("display","block");
    $("#device").find(".show-F9").css("display","flex");
   
  });
  $("#menu_select").find("li:eq(0)").click();
  //初始化DriverLE
  //初始化F9装备槽
  if ($("#device").find(".show-F9").length <= 0) {
    var  str = '<ul class="show-F9">\
    <li>L-1</li>\
    <li>L-2</li>\
    <li>L-3</li>\
    <li>L-4</li>\
    <li>L-5</li>\
    </ul>';
    $("#device").append(str);
    $("#device").find(".show-F9").css({"top": "-40px", "left": "290px", "display": "flex"});
  } else {
    $(".show-F9").css({"display": "flex"});
  }

  for (var i = 0; i < DEVICE.profile.DriverLE.length; i++) {
    var change_value = "";
    if (DEVICE.profile.DriverLE[i].GUID != "") {
      change_value_light = DEVICE.profile.DriverLE[i].Name;
      $("#device").find(".show-F9").find("li").eq(i).data("change-value-light", change_value_light);
      $("#device").find(".show-F9").find("li").eq(i).addClass("border");
    }
  }

  var index_no_configuration = $.multilang("no_config");
  var kb_lamp = $.multilang("kb_lamp");
  if ($("#device").find(".show-function").length <= 0) {
    var add = '<div class="show-function">\
    <div class="show-function-content">\
    '+kb_lamp+': <span class="text" id="light_config">' + index_no_configuration + '</span>\
    <span class="remove" id="light_remove">\
    </span>\
    </div>\
    </div>';
    $("#device").append(add);
  }
  //初始化静态灯效
  //renderStaticLamp();
  //初始化模式灯效
  renderLamp();

  //初始化颜色下拉选择设定功能
  $("#le_config_color_select").off('click').click(function(){
    if ($("#le_config_colors").is(":hidden")) {
      $("#le_config_colors").show();
      $("#le_color_dropdown").addClass("hover-up"); 
    } else {
      $("#le_config_colors").hide();
      $("#le_color_dropdown").removeClass("hover-up"); 
    }
    return false;
  });
  $(document).click(function(){
    $("#le_config_colors").hide();
    $("#le_color_dropdown").removeClass("hover-up"); 
  });
}

function initFuncEvent() { 

  //设置下去 
  $('#apply').on('click', function() {
    onProfileChanged();
  });

  //初始化模式灯光功能按钮
  $("#tools_lamp_1").find(".ximagebutton").ximagebutton({
    onClick: function(){
      $element = this;
      var index = $element.index();  
      $("#tools_lamp_2").find(".functions").hide();
      $("#tools_lamp_2").find(".functions").eq(index).css({'display': 'flex'});
      $element.ximagebutton('active');
      $element.siblings().ximagebutton('inactive');
    }
  });
  $("#device").find(".show-F9").find("li").unbind('click').click(function() {
    $(this).addClass("button-active").siblings().removeClass("button-active");
    var selectguid = $("#tools_lamp_2").find("ul li.selected").data('guid');
    if(selectguid !== DEVICE.profile.DriverLE[$(this).index()].GUID){
      $('#tool_lamp .confirm .yes').addClass("btn-breath");
    }else{
      $('#tool_lamp .confirm .yes').removeClass("btn-breath");
    }
  });

  $("#device").find(".show-F9").find("li").unbind('mouseenter mouseleave').hover(function() {
    clearTimeout(hoverTimer);
    var index = $(this).index();
    var change_value_light = $(this).data("change-value-light");
    var top = $(this).parent().position().top + $(this).position().top - 35;
    var left = $(this).parent().position().left + $(this).position().left + $(this).width() / 2 - 60 + 3;
    var $showButtonFunction = $("#device").find(".show-function");
    var $this = $(this);
    if (change_value_light) {
      $("#light_config").text(change_value_light)
    } else {
      $("#light_config").text($.multilang("no_config"));
    }
    $showButtonFunction.hover(function() {
      clearTimeout(hoverTimer);
    },function() {
      clearTimeout(hoverTimer);
      hoverTimer = setTimeout(function() {
        $("#device").find(".show-function").fadeOut("fast");
      }, 200);
    });

    $("#light_remove").unbind('click').click(function() {
      $("#light_config").text($.multilang("no_config"));
      $this.data("change-value-light",null);
      $this.removeClass("border");
      DEVICE.profile.DriverLE[index].GUID= "";
      DEVICE.profile.DriverLE[index].Name= "";
      window.parent.writeProfile(CMS.deviceID, DEVICE.profile.GUID, DEVICE.profile, function() {
        window.parent.apply(CMS.modelID, DEVICE.profile.GUID, function(result) {  window.parent.warning($.multilang("clear_success"));});
      });
    });

    $showButtonFunction.css({"top": top + "px", "left": left + "px"});
    $showButtonFunction.css({"display": "flex"});

    $this.addClass("button-hover").siblings().removeClass("button-hover");
  },function() {
    clearTimeout(hoverTimer);
    hoverTimer = setTimeout(function() {
      $("#device").find(".show-function").fadeOut("fast");
    }, 200);
    $(this).removeClass("button-hover");
  });
  
  $(".confirm .yes").unbind('click').click(function(){
      var _this = $(this);
      var driverleindex = $(".show-F9").find('li.button-active').index();
      if(driverleindex !== -1){
        var sellampguid = $("#tool_lamp .func-setting .menu-list").find(".menu-item.selected").data('guid');
        var sellampname = $("#tool_lamp .func-setting .menu-list").find(".menu-item.selected span").text();
        if(sellampguid){
          DEVICE.profile.DriverLE[driverleindex].GUID = sellampguid;
          DEVICE.profile.DriverLE[driverleindex].Name = sellampname;
          window.parent.writeProfile(CMS.deviceID, DEVICE.profile.GUID, DEVICE.profile, function() {
            $("#light_config").text(sellampname);
            $(".show-F9").find('li.button-active').data("change-value-light",sellampname);
            $(".show-F9").find('li.button-active').addClass("border");
            _this.removeClass("btn-breath");
            $('#apply').addClass("btn-breath");
          });
        }
      }else{
        window.parent.warning('请选择要设置的灯效顺序！');
      }
  });
  //主题灯效配置
  //staticLampFunc();
  lampfunc();
  bindingEvent();
  $(".show-F9 li").eq(0).click();
  var $selectedLamp = $("#tools_lamp_2").find("ul").find("li[data-guid='" + DEVICE.profile.DriverLE[0].GUID + "']");
  if($selectedLamp.length > 0){
    $selectedLamp.addClass("selected");
    $selectedLamp.parent().parent().scrollTop($("#tools_lamp_2").find("ul").find("li[data-guid='" + DEVICE.profile.DriverLE[0].GUID + "']").index()*32);
    $selectedLamp.click();
  }else{
    $("#tools_lamp_2 ul li").eq(0).click();
  }
}

function renderStaticLamp() {
  $("#tool_lamp").find(".static-lelist").find("ul").empty();
  var str = '<li class="menu-item menu-item-light" data-guid=""><span>'+$.multilang("kb_static_light")+'</span></li>';
  $("#tool_lamp").find(".static-lelist").find("ul").append(str);

}

function staticLampFunc() {
  $("#tool_lamp").find(".func-static-lelist").find("ul").find(".menu-item").unbind('click').click(function(){
    $(this).addClass("selected").siblings().removeClass("selected");
    $("#tool_lamp").find(".func-setting").find("ul").find(".menu-item").removeClass("selected");
    $.le('stop');
    DEVICE.playle = '';
    $("#le_config_color_select p").data('index', null);
    lightenKeyFunc(); 
    if(!DEVICE.profile.ModeLE.LEData) {
      DEVICE.profile.ModeLE.LEData = {};  
    }
    var leData = DEVICE.profile.ModeLE.LEData;
    DEVICE.leData = leData;
    DEVICE.params = null;
    
    var config = {};
    for(var index in leData) {
      config[index] = leData[index].replace("0x", "#");
    }
    var data = { 
      "config": config 
    }; 
    $('#device').device({
      display: data
    });
  });
}

function lightenKeyFunc() {
  $("#bottom_nav").css({'display': 'flex'});
  $("#le_configs").css({'display': 'none'});
  $('#device').device({ 
    onSingleSelect: null,
    onMultiSelect: null
  });
  $('#device').device({ 
    onSingleSelect: lighten,
    onMultiSelect: lightenMulti
  });
}

function lighten(keyItem) {
  var locationCode = keyItem.LocationCode;
  if (DEVICE.definecolor === null) {
    window.parent.warning($.multilang("kb_select_color"));
    return;
  }
  DEVICE.profile.ModeLE.LEData[locationCode] = DEVICE.definecolor;
  var leData = DEVICE.profile.ModeLE.LEData;
  var config = {};
  for(var index in leData) {
    config[index] = leData[index].replace("0x", "#");
  }
  var data = { 
    "config": config 
  }; 
  $('#device').device({
    display: data
  });

}

function lightenMulti(locationCodes) {
  if (DEVICE.definecolor === null) {
    window.parent.warning($.multilang("kb_select_color"));
    return;
  }
  for (var i = 0; i < locationCodes.length; i++) {
    var locationCode = locationCodes[i];   
    DEVICE.profile.ModeLE.LEData[locationCode] = DEVICE.definecolor;
  }
  var leData = DEVICE.profile.ModeLE.LEData;
  var config = {};
  for(var index in leData) {
    config[index] = leData[index].replace("0x", "#");
  }
  var data = { 
    "config": config 
  }; 
  $('#device').device({
    display: data
  });
}

function cancelKeyFunc() {
  $("#bottom_nav").css({'display': 'flex'});
  $('#device').device({
    'display': {
      'config': {}
    }
  });
  $('#device').device({ 
    onSingleSelect: null,
    onMultiSelect: null
  });
}

function renderLamp() {
  if (CMS.les.length ==0) {
    return;
  }
  $("#tool_lamp").find(".menu-list").find("ul").empty();
  var str = '';
  for (var i = 0; i < CMS.les.length; i++) {
    var combicostr = "";
    if(CMS.les[i].LeType === "combination"){
      combicostr = '<div class="combination-lamp"></div>';
      continue;
    }
    if (CMS.les[i].Type === 1) {
      str += '<li class="menu-item menu-item-light menu-item-dir" data-index="' + i + '" data-type="' + 1 + '">\
      <span>' + CMS.les[i].Name + '</span>\
      <div class="rmact" style="flex-direction: row;display: flex;align-items: center;">\
      <input type="text" value="" class="input-text">\
      <i class="ar-act"></i>\
      </div>\
      </li>';
    } else {
      if(CMS.deviceConfig.LeCate && CMS.les[i].LeCate && (CMS.deviceConfig.LeCate == CMS.les[i].LeCate)) {
        str += '<li class="menu-item menu-item-light menu-item-file" data-index="' + i + '" data-type="' + 0 + '" data-guid="' + CMS.les[i].GUID + '">\
        <span>' + CMS.les[i].Name + '</span>' + combicostr;
        str +='</div>\
        </li>';
      }
    }
  }
  $("#tool_lamp").find(".menu-list").find("ul").append(str);
}

function lampfunc() {
  $("#tool_lamp").find(".func-setting").find("ul").find(".menu-item").unbind('click').click(function(){
    $(this).addClass("selected").siblings().removeClass("selected");
    $("#tool_lamp").find(".func-static-lelist").find("ul").find(".menu-item").removeClass("selected");
    cancelKeyFunc();
    var guid = $(this).data("guid");
    if(guid) {
      window.parent.readLE(guid, function(data){
        var params = null;
        params = $.le('play', data, params);
        DEVICE.leData = data;
        DEVICE.params = null;
        setColorConfig(data, params);
        DEVICE.playle = guid;
      });
      if(guid !== DEVICE.profile.DriverLE[$(".show-F9").find('li.button-active').index()].GUID){
        $('#tool_lamp .confirm .yes').addClass("btn-breath");
      }else{
        $('#tool_lamp .confirm .yes').removeClass("btn-breath");
      }
    } else {
      $.le('stop');
      DEVICE.playle = '';
    }
  });
}

function setColorConfig(data, params) {
  $("#le_configs").css({'display': 'flex'});
  $("#le_config_colors").empty();
  $("#le_config_set").css({'display': 'none'});
  if (!params) return;
  if(params && params.hasOwnProperty('LEConfigs') && Object.prototype.toString.call(params.LEConfigs) == '[object Array]') {
    var leConfigs = params.LEConfigs;
    initLeColorSet(leConfigs);
    leColorSetFunc(data, params);
  }
}

function initLeColorSet(leConfigs) {
  var text = '颜色参数设置';
  $("#le_config_color_select p").text(text);
  $("#le_config_color_select p").data('index', null);
  var str = '';
  for (var i = 0; i < leConfigs.length; i++) {
    str += '<div class="item">' + leConfigs[i].Name + '</div>';
  }
  $("#le_config_colors").append(str);
}

function leColorSetFunc(data, params) {
  $("#le_config_colors").find(".item").off('mouseenter mouseleave').hover(function(){
    $(this).css({'outline': '1px solid #00c2ee'});
  }, function(){
    $(this).css({'outline': 'none'});
  });

  $("#le_config_colors").find(".item").off('click').click(function(){
    $("#le_config_colors").hide();
    $("#le_color_dropdown").removeClass("hover-up"); 
    var text = $(this).text();
    var index = $(this).index();
    $("#le_config_color_select p").text(text);
    $("#le_config_color_select p").data('index', index);
    DEVICE.params = params;
    $.le('play', data, DEVICE.params);
    $("#le_config_set").css({'display': 'flex'});
    $("#le_config_set_color").css({'backgroundColor': DEVICE.params.LEConfigs[index].Color.replace("0x", "#")});
  });
}

function speedSelect() {
  $("#speed_select").off('mouseup').mouseup(function(){
    var speed = parseInt($(this).val());
    DEVICE.profile.Speed = speed;
  });
}

function bindingEvent(){
  //取色器颜色变化
  $('.picker').each( function() {
    $(this).minicolors({
      inline: $(this).attr('data-inline') === 'true',
      change: function(hex, opacity) {
        onColorChanged(hex);
      },
      theme: 'default'
    });
  });

  //颜色块选择框点击
  $("#choose_color").find(".item").click(function(){
    $("#current_color").css({
      backgroundColor: $(this).css("background-color")
    });

    var rgb = $(this).css('background-color'); 
    rgb = jQuery.Color(rgb).toHexString();
    rgb = "0x"+rgb.substring(1,rgb.length);
    DEVICE.definecolor = rgb;
    var index = $("#le_config_color_select p").data('index');
    if (DEVICE.params !== null && index !== null) {
      DEVICE.params.LEConfigs[index].Color = DEVICE.definecolor;
      $.le('play', DEVICE.leData, DEVICE.params);
      $("#le_config_set_color").css({
        backgroundColor: $(this).css("background-color")
      });
    }
  });
   
}

//颜色改变回调
function onColorChanged(data){
  $("#current_color").css({
    'backgroundColor': data
  });
  
  DEVICE.definecolor = "0x"+data.substring(1,data.length);
  var index = $("#le_config_color_select p").data('index');
  if (DEVICE.params !== null && index !== null) {
    DEVICE.params.LEConfigs[index].Color = DEVICE.definecolor;
    $.le('play', DEVICE.leData, DEVICE.params);
    
    $("#le_config_set_color").css({
      'backgroundColor': data
    });
  }
}


function checkLE() {
  var ret = true;
  ret &= checkDriverLE();
  return ret;
}



//检查F9标准灯效
function checkDriverLE() {
  var isCompleted =true;
  for (var i = 0; i < DEVICE.profiles[2].DriverLE.length; i++) {
    if (!DEVICE.profiles[2].DriverLE[i].GUID)
      continue;
    var flag = true;  
    for (var j = 0; j < window.parent.CMS.les.length; j++) {
      if (DEVICE.profiles[2].DriverLE[i].GUID == window.parent.CMS.les[j].GUID) {
        if (DEVICE.profiles[2].DriverLE[i].Name != window.parent.CMS.les[j].Name) {
          DEVICE.profiles[2].DriverLE[i].Name = window.parent.CMS.les[j].Name;
        }
        flag = false;
        break;
      }
    }
    if (flag)
      isCompleted = false; 
  }
  return isCompleted;
}


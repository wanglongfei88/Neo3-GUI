/* eslint-disable */ 
import React from 'react';
import 'antd/dist/antd.css';
import '../../static/css/menu.css'
import '../../static/css/wallet.css'
import {Link} from 'react-router-dom';
import {  Layout, Menu, Icon, message } from 'antd';
import MenuDown from '../Common/menudown'
import Walletdetail from './walletdetail';

import {Router, Route, Switch} from 'react-router-dom';


const { Sider } = Layout;
const { SubMenu } = Menu;


class Walletlayout extends React.Component{
  constructor(props){
    super(props);
    this.state = {
        size: 'default'
    };
  }
  toHome = () =>{
    location.href=location.origin;
  }
  toPage = (e) =>{
    console.log(e.key);
    let _link = location.origin + "/Walletlist"
    switch(e.key){
      case 1:
    }
  }
  render = () =>{
    return (
      <div style={{ height: '100%'}}>
          <Sider style={{ height: '100%'}} >
            <Menu
              className="menu-scroll"
              theme="dark"
              defaultSelectedKeys={['1']}
              defaultOpenKeys={['sub1']}
              mode="inline"
            >
              <Menu.Item onClick={this.toHome}>
                <Icon type="home" />
                <span>主页</span>
              </Menu.Item>
              <SubMenu
                key="sub1"
                title={
                  <span>
                    <Icon type="radius-setting" />
                    <span>钱包</span>
                  </span>
                }
              >
                <Menu.Item key="1" onClick={this.toPage}>账户列表</Menu.Item>
                <Menu.Item key="2" onClick={this.toPage}>交易记录</Menu.Item>
                <Menu.Item key="3" onClick={this.toPage}>转账</Menu.Item>
              </SubMenu>
            </Menu>
            <MenuDown />
          </Sider>
      </div>
    );
  }
} 

export default Walletlayout;
# Requirements:
#   Install Turtlebot3 packages
#   Modify turtlebot3_waffle SDF:
#     1) Edit /opt/ros/$ROS_DISTRO/share/turtlebot3_gazebo/models/turtlebot3_waffle/model.sdf
#     2) Add
#          <joint name="camera_rgb_optical_joint" type="fixed">
#            <parent>camera_rgb_frame</parent>
#            <child>camera_rgb_optical_frame</child>
#            <pose>0 0 0 -1.57079632679 0 -1.57079632679</pose>
#            <axis>
#              <xyz>0 0 1</xyz>
#            </axis>
#          </joint> 
#     3) Rename <link name="camera_rgb_frame"> to <link name="camera_rgb_optical_frame">
#     4) Add <link name="camera_rgb_frame"/>
#     5) Change <sensor name="camera" type="camera"> to <sensor name="camera" type="depth">
#     6) Change image width/height from 1920x1080 to 640x480
# Example:
#   $ ros2 launch rtabmap_demos turtlebot3_sim_rgbd_demo.launch.py
#
#   Teleop:
#     $ ros2 run turtlebot3_teleop teleop_keyboard

#!/usr/bin/env python3
# turtlebot3_sim_rgbd_demo.launch.py  （已集成 static TF 绑定点云坐标系）

from ament_index_python.packages import get_package_share_directory

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription, OpaqueFunction
from launch.substitutions import LaunchConfiguration, PathJoinSubstitution
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch_ros.actions import Node            # ← 新增
from launch_ros.substitutions import FindPackageShare

import os


def launch_setup(context, *args, **kwargs):
    # -------------------------------------------------------------------------
    # TURTLEBOT3_MODEL 环境变量
    # -------------------------------------------------------------------------
    if 'TURTLEBOT3_MODEL' not in os.environ:
        os.environ['TURTLEBOT3_MODEL'] = 'waffle'

    # -------------------------------------------------------------------------
    # 目录
    # -------------------------------------------------------------------------
    pkg_turtlebot3_gazebo = get_package_share_directory('turtlebot3_gazebo')
    pkg_nav2_bringup      = get_package_share_directory('nav2_bringup')
    pkg_rtabmap_demos     = get_package_share_directory('rtabmap_demos')

    world = LaunchConfiguration('world').perform(context)

    nav2_params_file = PathJoinSubstitution(
        [FindPackageShare('rtabmap_demos'),
         'params', 'turtlebot3_rgbd_nav2_params.yaml'])

    # -------------------------------------------------------------------------
    # 子 launch 路径
    # -------------------------------------------------------------------------
    gazebo_launch = PathJoinSubstitution(
        [pkg_turtlebot3_gazebo, 'launch', f'turtlebot3_{world}.launch.py'])
    nav2_launch = PathJoinSubstitution(
        [pkg_nav2_bringup, 'launch', 'navigation_launch.py'])
    rviz_launch = PathJoinSubstitution(
        [pkg_nav2_bringup, 'launch', 'rviz_launch.py'])
    rtabmap_launch = PathJoinSubstitution(
        [pkg_rtabmap_demos, 'launch', 'turtlebot3', 'turtlebot3_rgbd.launch.py'])

    # -------------------------------------------------------------------------
    # IncludeLaunchDescription
    # -------------------------------------------------------------------------
    gazebo = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(gazebo_launch),
        launch_arguments=[
            ('x_pose', LaunchConfiguration('x_pose')),
            ('y_pose', LaunchConfiguration('y_pose'))
        ])

    nav2 = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(nav2_launch),
        launch_arguments=[
            ('use_sim_time', 'true'),
            ('params_file', nav2_params_file)
        ])

    rviz = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(rviz_launch))

    rtabmap = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(rtabmap_launch),
        launch_arguments=[
            ('localization', LaunchConfiguration('localization')),
            ('use_sim_time', 'true')
        ])

    # -------------------------------------------------------------------------
    # ★ 新增：静态 TF，把 Gazebo 点云帧 rigid 绑定到 camera_rgb_optical_frame
    # -------------------------------------------------------------------------
    static_tf = Node(
        package='tf2_ros',
        executable='static_transform_publisher',
        name='rgbd_sensor_static_tf',
        arguments=[
            '0', '0', '0',          # x y z
            '0', '0', '0',          # roll pitch yaw
            'camera_rgb_optical_frame',
            'waffle/camera_rgb_optical_frame/rgbd_sensor'
        ],
        output='log'
    )

    return [nav2, rviz, rtabmap, gazebo, static_tf]


# -----------------------------------------------------------------------------
# LaunchDescription
# -----------------------------------------------------------------------------
def generate_launch_description():
    return LaunchDescription([

        DeclareLaunchArgument(
            'localization', default_value='false',
            description='Launch in localization mode.'),

        DeclareLaunchArgument(
            'world', default_value='house',
            choices=['world', 'house',
                     'dqn_stage1', 'dqn_stage2',
                     'dqn_stage3', 'dqn_stage4'],
            description='Turtlebot3 gazebo world.'),

        DeclareLaunchArgument(
            'x_pose', default_value='-2.0',
            description='Initial X position of the robot.'),

        DeclareLaunchArgument(
            'y_pose', default_value='0.5',
            description='Initial Y position of the robot.'),

        OpaqueFunction(function=launch_setup)
    ])

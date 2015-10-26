﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;

namespace PracticalPattern.CommonTools
{

    //准备工作
    class Program
    {
        static void Main(string[] args)
        {
            ConfigurationTest tt = new ConfigurationTest();
            tt.Test();
            
            Console.ReadLine();
        }
    }

    //new()的替代品

    /*
     * 很多选择：
     * 使用ObjectBuilder库（企业级）
     * 强迫所有需要创建的对象都支持where T:new()（去掉）
     * 做一个轻量级的对象构造 泵
     */

    /// <summary>
    /// 根据类型名称生成类型实例
    /// </summary>
    public interface IObjectBuilder
    {
        /// <summary>
        /// 创建类型实例
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="args"></param>
        /// <returns></returns>
        T BuildUp<T>(params object[] args);

        T BuildUp<T>() where T : new();

        /// <summary>
        /// 按照目标返回的类型，加工制定类型名称对应的类型实例
        /// 目标类型可以为接口、抽象类等抽象类型，typeName一般为实体类名称
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="typeName"></param>
        /// <returns></returns>
        T BuildUp<T>(string typeName);

        /// <summary>
        /// a按照目标类型，通过调用指定名称类型的构造函数，生成目标类型实例 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="typeName"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        T BuildUp<T>(string typeName,params object[] args);
    }


    //TypeCreator类，portable typeCreator 一个轻便的IObjectBuilder实现

    public class TypeCreator:IObjectBuilder
    {

        public T BuildUp<T>(params object[] args)
        {
            object result = Activator.CreateInstance(typeof(T), args);
            return (T)result;
        }

        public T BuildUp<T>() where T : new()
        {
            return Activator.CreateInstance<T>();
        }

        public T BuildUp<T>(string typeName)
        {
            return (T)Activator.CreateInstance(Type.GetType(typeName));
        }

        public T BuildUp<T>(string typeName, params object[] args)
        {
            return (T)Activator.CreateInstance(Type.GetType(typeName), args);
        }
    }




    //***********************************************************************
    //准备一个轻量级的内存Cache

    /*
     * Cache虽然可以简单至一个Dictionary，复杂至Enterprise Library的Cache Block
     * 
     * 这里用一个potable cache
     * 
     * 直接使用Dictionary<TKey,TValue>作为缓冲容器
     * 
     * 考虑到普遍而言Cache，读频率大于写频率（读频率较高才使用缓存），使用多读单写锁（System.Threading.ReaderWriterLock）控制并发同步
     * 
     * 使用上仅提供Dictionary<TKey,TValue>集合最基本的几个操作，而且没有使用Remove()方法，仅使用一个Clear()方法
     */



    /// <summary>
    /// 线程安全的轻量泛型类提供了从一组键到一组值的映射
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class GenericCache<TKey,TValue>
    {
        /// <summary>
        /// 内部的Dcitionary容器
        /// </summary>
        private Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();

        /// <summary>
        /// 用于并发同步访问的RW锁对象
        /// </summary>
        private System.Threading.ReaderWriterLock rwLock = new System.Threading.ReaderWriterLock();

        /// <summary>
        /// 一个TimeSpan，用于指定超时时间
        /// </summary>
        private readonly TimeSpan lockTimeOut = TimeSpan.FromMilliseconds(100);




        /// <summary>
        /// 将指定的键和值添加到字典中
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Add(TKey key,TValue value)
        {
            bool isExisting = false;
            rwLock.AcquireReaderLock(lockTimeOut);

            try
            {
                if (!dictionary.ContainsKey(key))
                    dictionary.Add(key, value);
                else
                    isExisting = true;
            }
            finally
            {
                rwLock.ReleaseWriterLock();
            }
            if (isExisting)
                throw new IndexOutOfRangeException();
        }


        /// <summary>
        /// 获取指定的键相关联值
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetValue(TKey key,out TValue value)
        {
            rwLock.AcquireReaderLock(lockTimeOut);
            bool result;
            try
            {
                result = dictionary.TryGetValue(key, out value);
            }
            finally 
            {
                rwLock.ReleaseReaderLock();
            }
            return result;
        }

        /// <summary>
        /// 移除键值对
        /// </summary>
        public void Clear()
        {
            if(dictionary.Count>0)
            {
                rwLock.AcquireWriterLock(lockTimeOut);
                try
                {
                    dictionary.Clear();
                }
                finally
                {
                    rwLock.ReleaseWriterLock();
                }
            }
        }



        public bool ContainsKey(TKey key)
        {
            if (dictionary.Count <= 0) return false;
            bool result;
            try
            {
                result = dictionary.ContainsKey(key);
            }
            finally
            {
                rwLock.ReleaseReaderLock();
            }
            return result;
        }

        public int Count
        {
            get
            {
                return dictionary.Count;
            }
        }

     }



    //*************************************************
    /*
     * 准备一个集中访问配置文件的Broker
     * 
     * 为了确保所有的模式实例均通过一个统一的出口访问配置文件，并向客户程序隔离具体配置文件的组成方式，增加一个ConfigurationBroker类来集中管理所有的配置对象实例。同时为了便于客户程序中无需使用System.Configuration,模式库会对每个配直接增加自己的配置对象接口，而不是基于System.Configuration自带的基类，并有所在的配直接组通过实现IConfiguration接口，当ConfigurationBroker回调Load()方法的时候加载到ConfigurationBroker的配置对象缓存里。
     */


    /// <summary>
    /// 定义每个配直节组的抽象动作
    /// </summary>
    public interface IConfigurationSource
    {
        /// <summary>
        /// ConfigurationBroker可以通过回调方法，加载每个配直节组需要缓冲的配置对象
        /// </summary>
        void Load();
    }

    public static class ConfigurationBroker
    {

        /// <summary>
        /// 用于保存所有需要等级的通过配置获取的类型实体，使用线程安全的内存缓存对象保存
        /// </summary>
        private static readonly GenericCache<Type, object> cache;

        /// <summary>
        /// 构造函数，构造配置解析器并初始化配置缓存
        /// </summary>
        static ConfigurationBroker()
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            cache = new GenericCache<Type, object>();

            //foreach (ConfigurationSectionGroup group in config.SectionGroups)
            //{
            //    Console.WriteLine(group.GetType().ToString());
            //}

            //查找自定义的IConfigurationSource配置组节，并调用Load方法加载配置缓存对象
            foreach (ConfigurationSectionGroup group in config.SectionGroups)
                if (typeof(IConfigurationSource).IsAssignableFrom(group.GetType()))
                {
                    ((IConfigurationSource)group).Load();
                }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="item"></param>
        public static void Add(Type type,object item)
        {
            if ((type == null) || (item == null))
                throw new NullReferenceException();
            cache.Add(type,item);
        }

        public static void Add(KeyValuePair<Type,object> item)
        {
            Add(item.Key, item.Value);
        }
        public static void Add(object item)
        {
            Add(item.GetType(), item);
        }



        public static T GetConfigurationObject<T>() where T : class
        {
            if (cache.Count <= 0)
                return null;
            object result;
            if (!cache.TryGetValue(typeof(T), out result))
                return null;
            else
                return (T)result;
        }



    }



    //测试类
    public class ConfigurationTest
    {
        public void Test()
        {

            IObjectBuilder builder = ConfigurationBroker.GetConfigurationObject<IObjectBuilder>();

            //Assert.IsNotNull(builder);

            if (builder == null)
            {
                Console.WriteLine("builder is null");
            }
            else
            {
                Console.WriteLine("builder is : ");
                Console.WriteLine(builder);
            }

        }
    }





}

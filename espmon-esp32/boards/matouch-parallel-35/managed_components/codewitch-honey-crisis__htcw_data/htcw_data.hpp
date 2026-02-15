#pragma once
#include <stdlib.h>
#include <string.h>
namespace data {
// dynamic vector of scalar (constructorless) data
template <typename T>
class simple_vector final {
    void* (*m_allocator)(size_t);
    void* (*m_reallocator)(void*, size_t);
    void (*m_deallocator)(void*);
    size_t m_size;  // size in number of T values
    T* m_begin;
    size_t m_capacity;  // allocated size in T values
    bool resize(size_t new_size) {
        size_t capacity = new_size;
        if (capacity > this->m_capacity) {
            size_t newcap = capacity + (this->m_capacity >> 1u);
            void* data;
            if (this->m_begin) {
                data = m_reallocator(this->m_begin, newcap * sizeof(T));
            } else {
                newcap = 8;
                const size_t newsz = newcap * sizeof(T);
                data = m_allocator(newsz);
            }
            if (data) {
                this->m_capacity = newcap;
                this->m_begin = (T*)data;
            } else
                return false;  // error: not enough memory
        }
        return true;
    }

   public:
    using value_type = T;
    using iterator = value_type*;
    using const_iterator = const value_type*;
    class accessor {
        friend class simple_vector;
        T* p;
        accessor(T* ptr) : p(ptr) {
        }

       public:
        accessor& operator=(const T& value) {
            *p = value;
            return *this;
        }
        operator T() const {
            return *p;
        }
    };
    simple_vector(void*(allocator)(size_t) = ::malloc,
                  void*(reallocator)(void*, size_t) = ::realloc,
                  void(deallocator)(void*) = ::free) : m_allocator(allocator),
                                                       m_reallocator(reallocator),
                                                       m_deallocator(deallocator) {
        m_begin = nullptr;
        m_size = 0;
        m_capacity = 0;
    }
    accessor operator[](size_t index) {
        return accessor(m_begin + index);
    }
    ~simple_vector() {
        m_size = 0;
        if (m_begin != nullptr) {
            m_deallocator(m_begin);
            m_begin = nullptr;
        }
    }
    inline size_t size() const { return m_size; }
    inline size_t capacity() const { return m_capacity; }
    inline bool reserve(size_t new_size) {
        return resize(new_size);
    }
    inline const_iterator cbegin() const { return m_begin; }
    inline const_iterator cend() const { return m_begin==nullptr?nullptr:m_begin + m_size; }
    inline iterator begin() { return m_begin; }
    inline iterator end() { return m_begin==nullptr?nullptr: m_begin + m_size; }
    void clear(bool keep_capacity = false) {
        if (keep_capacity) {
            m_size = 0;
            return;
        }
        if (m_begin) {
            m_size = 0;
            m_capacity = 0;
            m_deallocator(m_begin);
            m_begin = nullptr;
        }
    }
    bool push_back(const T& value) {
        if (!resize(m_size + 1)) {
            return false;
        }

        m_begin[m_size++] = value;
        return true;
    }
    bool push_multi_raw(const T* values, size_t values_size) {
        if(values_size==0) { return true; }
        if(values==nullptr) { return false; }
        if (!resize(m_size + values_size)) {
            return false;
        }
        memcpy(m_begin,values,values_size * sizeof(T));
        m_size += values_size;
        return true;
    }
    void erase(iterator first, iterator last) {
        if (last < first) {
            iterator tmp;
            tmp = first;
            first = last;
            last = tmp;
        }
        size_t start = first - m_begin;
        size_t end = last - m_begin;
        size_t erase_size = end - start + 1;
        if (last < m_begin + m_size - 1) {
            const size_t count = m_size - end;
            for(size_t i = 0;i<count;++i) {
                first[i]=*(last+1+i);
            }
            //memmove(first, last + 1, sizeof(T) * count);
        }
        m_size -= erase_size;
    }
};

// dynamic stack of scalar (constructorless) data
template <typename T>
class simple_stack final {
    void* (*m_allocator)(size_t);
    void* (*m_reallocator)(void*, size_t);
    void (*m_deallocator)(void*);
    size_t m_size;  // size in number of T values
    T* m_begin;
    size_t m_capacity;  // allocated size in T values
    bool resize(size_t new_size) {
        size_t capacity = new_size;
        if (capacity > this->m_capacity) {
            size_t newcap = capacity + (this->m_capacity >> 1u);
            void* data;
            if (this->m_begin) {
                data = m_reallocator(this->m_begin, newcap * sizeof(T));
            } else {
                newcap = 8;
                const size_t newsz = newcap * sizeof(T);
                data = m_allocator(newsz);
            }
            if (data) {
                this->m_capacity = newcap;
                this->m_begin = (T*)data;
            } else
                return false;  // error: not enough memory
        }
        this->m_size = new_size;
        return true;
    }

   public:
    using value_type = T;
    using iterator = value_type*;
    using const_iterator = const value_type*;
    simple_stack(void*(allocator)(size_t) = ::malloc,
                 void*(reallocator)(void*, size_t) = ::realloc,
                 void(deallocator)(void*) = ::free) : m_allocator(allocator),
                                                      m_reallocator(reallocator),
                                                      m_deallocator(deallocator) {
        m_begin = nullptr;
        m_size = 0;
        m_capacity = 0;
    }
    ~simple_stack() {
        m_size = 0;
        if (m_begin != nullptr) {
            m_deallocator(m_begin);
            m_begin = nullptr;
        }
    }
    inline size_t size() const { return m_size; }
    inline size_t capacity() const { return m_capacity; }
    inline const_iterator cbegin() const { return m_begin; }
    inline const_iterator cend() const { return m_begin + m_size; }
    inline iterator begin() { return m_begin; }
    inline iterator end() { return m_begin + m_size; }
    void clear() {
        if (m_begin) {
            m_size = 0;
            m_capacity = 0;
            m_deallocator(m_begin);
            m_begin = nullptr;
        }
    }
    bool push(const T& value) {
        if (!resize(m_size + 1)) {
            return false;
        }
        m_begin[m_size - 1] = value;
        return true;
    }
    T* pop() {
        if (!m_size) {
            return nullptr;
        }
        return &m_begin[--m_size];
    }
    T* peek(size_t index) {
        if (index >= m_size) {
            return nullptr;
        }
        return &m_begin[m_size - index - 1];
    }
};
// a key value pair
template <typename TKey, typename TValue>
struct simple_pair final {
    TKey key;
    TValue value;
    simple_pair(TKey key, TValue value) : key(key), value(value) {
    }
    simple_pair(const simple_pair& rhs) {
        key = rhs.key;
        value = rhs.value;
    }
    simple_pair& operator=(const simple_pair& rhs) {
        key = rhs.key;
        value = rhs.value;
        return *this;
    }
    simple_pair(simple_pair&& rhs) {
        key = rhs.key;
        value = rhs.value;
    }
    simple_pair& operator=(simple_pair&& rhs) {
        key = rhs.key;
        value = rhs.value;
        return *this;
    }
};
// a simple hash table
template <typename TKey, typename TValue, size_t Size>
class simple_fixed_map final {
    static_assert(Size > 0, "Size must be a positive integer");
    using bucket_type = simple_vector<simple_pair<TKey, TValue>>;
    bucket_type m_buckets[Size];
    int (*m_hash_function)(const TKey&);
    size_t m_size;

   public:
    simple_fixed_map(int(hash_function)(const TKey&),
                     void*(allocator)(size_t) = ::malloc,
                     void*(reallocator)(void*, size_t) = ::realloc,
                     void(deallocator)(void*) = ::free) : m_hash_function(hash_function), m_size(0) {
        for (size_t i = 0; i < (int)Size; ++i) {
            m_buckets[i] = bucket_type(allocator, reallocator, deallocator);
        }
    }
    using key_type = TKey;
    using mapped_type = TValue;
    using value_type = simple_pair<TKey, TValue>;
    inline size_t size() const { return m_size; }
    void clear() {
        m_size = 0;
        for (size_t i = 0; i < Size; ++i) {
            m_buckets->clear();
        }
    }
    bool insert(const value_type& value) {
        int h = m_hash_function(value.key) % Size;
        bucket_type& bucket = m_buckets[h];
        if (bucket.size()) {
            auto it = bucket.cbegin();
            while (it != bucket.cend()) {
                if (it->key == value.key) {
                    return false;
                }
                ++it;
            }
        }

        if (bucket.push_back({value.key, value.value})) {
            ++m_size;
            return true;
        }
        return false;
    }
    bool remove(const key_type& key) {
        int h = m_hash_function(key) % Size;
        bucket_type& bucket = m_buckets[h];
        if(bucket.size()) {
            auto it = bucket.begin();
            while (it != bucket.end()) {
                if (it->key == key) {
                    bucket.erase(it,it);
                    --m_size;
                    return true;
                }
                ++it;
            }
        }
        return false;
    }
    value_type* at(size_t index) {
        size_t i = 0;
        for(size_t j=0;j<Size;++j) {
            bucket_type& bucket = m_buckets[j];
            for(typename bucket_type::iterator it = bucket.begin();it!=bucket.end();++it) {
                if(index==i) {
                    return it;
                }
                ++i;
            }
        }
        return nullptr;
    }
    mapped_type* find(const key_type& key) {
        int h = m_hash_function(key) % Size;
        bucket_type& bucket = m_buckets[h];
        if (bucket.size()) {
            auto it = bucket.begin();
            while (it != bucket.end()) {
                if (it->key == key) {
                    return &it->value;
                }
                ++it;
            }
        }
        return nullptr;
    }
    mapped_type* find_mutable(const key_type& key) {
        int h = m_hash_function(key) % Size;
        bucket_type& bucket = m_buckets[h];
        if (bucket.size()) {
            auto it = bucket.begin();
            while (it != bucket.end()) {
                if (it->key == key) {
                    return &it->value;
                }
                ++it;
            }
        }
        return nullptr;
    }
};
// a circular buffer
template <typename T, size_t Capacity>
class circular_buffer final {
    T m_data[Capacity];
    size_t m_head;
    size_t m_tail;
    bool m_full;
    void advance() {
        if (m_full) {
            if (++(m_tail) == capacity) {
                m_tail = 0;
            }
        }

        if (++(m_head) == capacity) {
            m_head = 0;
        }
        m_full = (m_head == m_tail);
    }
    void retreat() {
        m_full = false;
        if (++(m_tail) == capacity) {
            m_tail = 0;
        }
    }

   public:
    using type = circular_buffer;
    using value_type = T;
    constexpr static const size_t capacity = Capacity;

    inline circular_buffer() {
        clear();
    }
    inline bool empty() const {
        return (!m_full && (m_head == m_tail));
    }
    inline bool full() const {
        return m_full;
    }
    size_t size() const {
        size_t result = capacity;
        if (!m_full) {
            if (m_head >= m_tail) {
                result = (m_head - m_tail);
            } else {
                result = (capacity + m_head - m_tail);
            }
        }
        return result;
    }
    inline void clear() {
        m_head = 0;
        m_tail = 0;
        m_full = false;
    }
    void put(const value_type& value) {
        m_data[m_head] = value;
        advance();
    }
    const value_type* peek(size_t index = 0) const {
        if (!empty()) {
            return m_data + ((m_tail + index) % size());
        }
        return nullptr;
    }
    bool get(value_type* out_value) {
        if (!empty()) {
            if (out_value != nullptr) {
                *out_value = m_data[m_tail];
            }
            retreat();
            return true;
        }
        return false;
    }
};
template <typename T>
class simple_list {
    void* (*m_allocator)(size_t);
    void (*m_deallocator)(void*);
    size_t m_size;  // size in number of T values
    struct entry final {
        T value;
        entry* next;
    };
    entry* m_head;
    entry* m_tail;

   public:
    using type = simple_list;
    using value_type = T;
    using reference = value_type&;
    using pointer = value_type*;
    class iterator final {
        entry* m_current;

       public:
        iterator(entry* current) : m_current(current) {}
        using value_type = T;
        reference operator*() const { return m_current->value; }
        pointer operator->() { return &m_current->value; }
        // Prefix increment
        iterator& operator++() {
            m_current = m_current->next;
            return *this;
        }
        // Postfix increment
        iterator operator++(int) {
            iterator tmp = *this;
            ++(*this);
            return tmp;
        }
        bool operator!() {
            return m_current == nullptr;
        }
        friend bool operator==(const iterator& lhs, const iterator& rhs) { return lhs.m_current == rhs.m_current; };
        friend bool operator!=(const iterator& lhs, const iterator& rhs) { return lhs.m_current != rhs.m_current; };
    };
    class const_iterator final {
        friend class simple_list;
        simple_list* m_parent;
        entry* m_current;

       public:
        using value_type = T;
        const reference operator*() const { return m_current->value; }
        pointer operator->() const { return &m_current->value; }
        // Prefix increment
        const_iterator& operator++() const {
            m_current = m_current->next;
            return *this;
        }
        // Postfix increment
        const_iterator operator++(int) {
            const_iterator tmp = *this;
            ++(*this);
            return tmp;
        }
        bool operator!() const {
            return m_current == nullptr;
        }
        friend bool operator==(const const_iterator& lhs, const const_iterator& rhs) { return lhs.m_current == rhs.m_current; };
        friend bool operator!=(const const_iterator& lhs, const const_iterator& rhs) { return lhs.m_current != rhs.m_current; };
    };

    simple_list(void*(allocator)(size_t) = ::malloc,
                void(deallocator)(void*) = ::free) : m_allocator(allocator),
                                                     m_deallocator(deallocator) {
        m_head = nullptr;
        m_tail = nullptr;
        m_size = 0;
    }
    size_t size() const {
        return m_size;
    }
    bool push_back(const T& value) {
        entry* p = (entry*)m_allocator(sizeof(entry));
        if (p == nullptr) {
            return false;
        }
        p->value = value;
        p->next = nullptr;
        if (m_head == nullptr) {
            m_head = p;
            m_tail = p;
            m_size = 1;
            return true;
        }
        ++m_size;
        m_tail->next = p;
        m_tail = p;
        return true;
    }
    iterator begin() { return iterator(m_head); }
    iterator end() { return iterator(m_tail->next); }
    const_iterator cbegin() const { return const_iterator(m_head); }
    const_iterator cend() const { return const_iterator(m_tail->next); }
    bool push_front(const T& value) {
        entry* p = (entry*)m_allocator(sizeof(entry));
        if (p == nullptr) {
            return false;
        }
        p->value = value;
        p->next = m_head;
        if (m_head == nullptr) {
            m_head = p;
            m_tail = p;
            m_size = 1;
            return true;
        }
        ++m_size;
        m_head = p;
        return true;
    }
    void clear() {
        entry* p = m_head;
        while (p != nullptr) {
            entry* n = p->next;
            m_deallocator(p);
            p = n;
        }
        m_size = 0;
    }
    virtual ~simple_list() {
        clear();
    }
};

}  // namespace data